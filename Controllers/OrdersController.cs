using Microsoft.AspNetCore.Mvc;
using MotoBikeStore.Models;
using MotoBikeStore.Services;
using System.Linq;

namespace MotoBikeStore.Controllers
{
    public class OrdersController : Controller
    {
        const string CART_KEY = "CART_ITEMS";
        const string USER_KEY = "CURRENT_USER";

        // GET: /Orders/Checkout
        public IActionResult Checkout()
        {
            
            var ids = HttpContext.Session.GetObjectFromJson<List<int>>(CART_KEY) ?? new List<int>();
            if (!ids.Any())
            {
                TempData["ErrorMessage"] = "Giỏ hàng trống!";
                return RedirectToAction("Index", "Cart");
            }

            var products = InMemoryDataStore.Products.Where(p => ids.Contains(p.Id)).ToList();
            ViewBag.Products = products;
            ViewBag.Subtotal = products.Sum(p => p.Price);

            return View(new Order());
        }

        // POST: /Orders/Checkout
 // POST: /Orders/Checkout
[HttpPost]
[ValidateAntiForgeryToken]
public IActionResult Checkout(Order order, string? couponCode)
{
    var ids = HttpContext.Session.GetObjectFromJson<List<int>>(CART_KEY) ?? new List<int>();
    var products = InMemoryDataStore.Products.Where(p => ids.Contains(p.Id)).ToList();

    ViewBag.Products = products;
    ViewBag.Subtotal = products.Sum(p => p.Price);

    if (!ids.Any())
    {
        TempData["ErrorMessage"] = "Giỏ hàng trống!";
        return RedirectToAction("Index", "Cart");
    }

    // ✅ VALIDATE
    bool hasError = false;

    if (string.IsNullOrWhiteSpace(order.CustomerName))
    {
        ModelState.AddModelError("CustomerName", "Vui lòng nhập họ tên");
        hasError = true;
    }

    if (string.IsNullOrWhiteSpace(order.Phone))
    {
        ModelState.AddModelError("Phone", "Vui lòng nhập số điện thoại");
        hasError = true;
    }
    else if (!System.Text.RegularExpressions.Regex.IsMatch(order.Phone, @"^[0-9]{10,11}$"))
    {
        ModelState.AddModelError("Phone", "Số điện thoại phải có 10-11 chữ số");
        hasError = true;
    }

    if (string.IsNullOrWhiteSpace(order.Address))
    {
        ModelState.AddModelError("Address", "Vui lòng nhập địa chỉ giao hàng");
        hasError = true;
    }

    if (string.IsNullOrWhiteSpace(order.PaymentMethod))
    {
        ModelState.AddModelError("PaymentMethod", "Vui lòng chọn hình thức thanh toán");
        hasError = true;
    }

    if (hasError)
    {
        return View(order);
    }

    // ✅ TẠO ĐƠN HÀNG
    order.Id = InMemoryDataStore.GetNextOrderId();
    order.OrderCode = $"MB-{DateTime.UtcNow:yyyyMMdd}-{order.Id:D4}";
    order.Details = products.Select(p => new OrderDetail
    {
        Id = InMemoryDataStore.GetNextOrderDetailId(),
        OrderId = order.Id,
        ProductId = p.Id,
        Quantity = 1,
        UnitPrice = p.Price
    }).ToList();

    order.Subtotal = products.Sum(p => p.Price);
    order.ShippingFee = order.Subtotal >= 5_000_000 ? 0 : 150_000;
    order.DiscountAmount = 0;

    // ✨ TỰ ĐỘNG ÁP DỤNG KHUYẾN MÃI
    Coupon? appliedCoupon = null;
    
    // 1️⃣ Nếu user nhập mã → Ưu tiên mã đó
    if (!string.IsNullOrWhiteSpace(couponCode))
    {
        appliedCoupon = InMemoryDataStore.Coupons.FirstOrDefault(c => 
            c.Code.Equals(couponCode, StringComparison.OrdinalIgnoreCase) && 
            c.IsActive && 
            c.StartDate <= DateTime.UtcNow && 
            c.EndDate >= DateTime.UtcNow &&
            order.Subtotal >= c.MinOrderAmount &&
            (c.UsageLimit == 0 || c.UsedCount < c.UsageLimit)
        );
    }
    
    // 2️⃣ Nếu không có mã hoặc mã không hợp lệ → Tự động tìm mã tốt nhất
    if (appliedCoupon == null)
    {
        var availableCoupons = InMemoryDataStore.Coupons
            .Where(c => 
                c.IsActive && 
                c.StartDate <= DateTime.UtcNow && 
                c.EndDate >= DateTime.UtcNow &&
                order.Subtotal >= c.MinOrderAmount &&
                (c.UsageLimit == 0 || c.UsedCount < c.UsageLimit)
            )
            .ToList();
        
        decimal maxDiscount = 0;
        foreach (var c in availableCoupons)
        {
            decimal discount = 0;
            if (c.DiscountPercent > 0)
            {
                discount = order.Subtotal * c.DiscountPercent / 100;
                if (c.MaxDiscountAmount.HasValue && discount > c.MaxDiscountAmount.Value)
                    discount = c.MaxDiscountAmount.Value;
            }
            else if (c.DiscountAmount.HasValue)
            {
                discount = c.DiscountAmount.Value;
            }
            
            if (discount > maxDiscount)
            {
                maxDiscount = discount;
                appliedCoupon = c;
            }
        }
    }
    
    // 3️⃣ Áp dụng coupon tốt nhất
    if (appliedCoupon != null)
    {
        if (appliedCoupon.DiscountPercent > 0)
        {
            order.DiscountAmount = order.Subtotal * appliedCoupon.DiscountPercent / 100;
            if (appliedCoupon.MaxDiscountAmount.HasValue && 
                order.DiscountAmount > appliedCoupon.MaxDiscountAmount.Value)
            {
                order.DiscountAmount = appliedCoupon.MaxDiscountAmount.Value;
            }
        }
        else if (appliedCoupon.DiscountAmount.HasValue)
        {
            order.DiscountAmount = appliedCoupon.DiscountAmount.Value;
        }
        
        order.CouponId = appliedCoupon.Id;
        appliedCoupon.UsedCount++;
        
        TempData["CouponApplied"] = $"Đã áp dụng mã {appliedCoupon.Code} - Giảm {order.DiscountAmount:N0}₫";
    }

    order.Total = order.Subtotal + order.ShippingFee - order.DiscountAmount;
    order.OrderDate = DateTime.UtcNow;
    order.Status = "Pending";

    var sess = HttpContext.Session.GetObjectFromJson<UserSession>(USER_KEY);
    if (sess != null) order.UserId = sess.Id;

    InMemoryDataStore.Orders.Add(order);
    HttpContext.Session.Remove(CART_KEY);

    Console.WriteLine($"[ORDER] Created: ID={order.Id}, Code={order.OrderCode}, Method={order.PaymentMethod}");

    // ✅ REDIRECT DỰA TRÊN PAYMENT METHOD
    if (order.PaymentMethod == "Chuyển khoản")
    {
        return RedirectToAction("BankTransfer", new { id = order.Id });
    }
    else // COD
    {
        // Populate Coupon cho Success page
        if (order.CouponId.HasValue)
        {
            order.Coupon = InMemoryDataStore.Coupons.FirstOrDefault(c => c.Id == order.CouponId.Value);
        }
        
        // Populate Products cho Success page
        foreach (var detail in order.Details)
        {
            detail.Product = InMemoryDataStore.Products.FirstOrDefault(p => p.Id == detail.ProductId);
        }
        
        return View("Success", order);
    }
}
        // GET: /Orders/BankTransfer/{id}
public IActionResult BankTransfer(int id)
{
    var order = InMemoryDataStore.Orders.FirstOrDefault(o => o.Id == id);
    if (order == null) return NotFound();
    
    // Populate Products
    foreach (var detail in order.Details)
    {
        detail.Product = InMemoryDataStore.Products.FirstOrDefault(p => p.Id == detail.ProductId);
    }
    
    // Populate Coupon
    if (order.CouponId.HasValue)
    {
        order.Coupon = InMemoryDataStore.Coupons.FirstOrDefault(c => c.Id == order.CouponId.Value);
    }
    
    return View(order);
}

        // Theo dõi đơn: nhận cả Id hoặc OrderCode
        public IActionResult Track(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return View((Order?)null);

            Order? order = null;
            // thử theo OrderCode
            order = InMemoryDataStore.Orders.FirstOrDefault(o => o.OrderCode.Equals(id, StringComparison.OrdinalIgnoreCase));
            // nếu không phải mã, thử parse int Id
            if (order == null && int.TryParse(id, out var orderId))
                order = InMemoryDataStore.Orders.FirstOrDefault(o => o.Id == orderId);

            if (order != null)
            {
                foreach (var d in order.Details)
                    d.Product = InMemoryDataStore.Products.FirstOrDefault(p => p.Id == d.ProductId);
            }
            return View(order);
        }

        public IActionResult MyOrders()
        {
            var sess = HttpContext.Session.GetObjectFromJson<UserSession>(USER_KEY);
            if (sess == null) return RedirectToAction("Login", "Auth");

            var userId = (int)sess.Id;
            var orders = InMemoryDataStore.Orders
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.OrderDate)
                .ToList();

            // map product cho list
            foreach (var o in orders)
                foreach (var d in o.Details)
                    d.Product = InMemoryDataStore.Products.FirstOrDefault(p => p.Id == d.ProductId);

            return View(orders);
        }
        

    }
}
