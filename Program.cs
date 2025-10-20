using MotoBikeStore.Models;
using MotoBikeStore.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ============ SERVICES ============
builder.Services.AddControllersWithViews();

// Session - BẮT BUỘC
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o => {
    o.Cookie.Name = ".MotoBikeStore.Session";
    o.IdleTimeout = TimeSpan.FromHours(4);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
});

builder.Services.AddDbContext<MotoBikeContext>(opt =>
    opt.UseInMemoryDatabase("MotoDb"));

var app = builder.Build();

// ============ MIDDLEWARE ============
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession(); // ⚠️ QUAN TRỌNG
app.UseAuthorization();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ============ INITIALIZE DATA ============
InMemoryDataStore.Initialize();
Console.WriteLine("✓ In-Memory Data Store initialized");
Console.WriteLine($"✓ Products: {InMemoryDataStore.Products.Count}");
Console.WriteLine($"✓ Categories: {InMemoryDataStore.Categories.Count}");
Console.WriteLine($"✓ Users: {InMemoryDataStore.Users.Count}");
Console.WriteLine($"✓ Coupons: {InMemoryDataStore.Coupons.Count}");

// ✅ KHỞI TẠO SEASONAL PROMOTIONS
SeasonalPromotionService.Initialize();
Console.WriteLine($"✓ Seasonal Promotions: {SeasonalPromotionService.GetAll().Count}");

app.Run();