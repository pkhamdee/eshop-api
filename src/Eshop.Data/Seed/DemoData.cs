using Eshop.Domain.Branches;
using Eshop.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Eshop.Data.Seed;

/// <summary>
/// Demo catalog and Branches, inserted once into an empty database after migrations run.
/// Staff edit these afterwards; seeding never overwrites existing rows.
/// </summary>
public static class DemoData
{
    public static async Task SeedAsync(DbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Set<ProductCategory>().AnyAsync(cancellationToken))
        {
            db.AddRange(ProductCategories());
        }

        if (!await db.Set<Branch>().AnyAsync(cancellationToken))
        {
            db.AddRange(Branches());
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static void Seed(DbContext db)
    {
        if (!db.Set<ProductCategory>().Any())
        {
            db.AddRange(ProductCategories());
        }

        if (!db.Set<Branch>().Any())
        {
            db.AddRange(Branches());
        }

        db.SaveChanges();
    }

    private static IEnumerable<ProductCategory> ProductCategories() =>
    [
        Category("mobile-phone", "มือถือ", 0.015m, 24, 1,
            Product("iPhone 17 128GB", "ไอโฟนรุ่นล่าสุด หน้าจอ 6.3 นิ้ว กล้องคู่ 48MP", 29_900m, "demo-iphone-17.svg"),
            Product("Samsung Galaxy A57 5G 256GB", "จอ Super AMOLED 6.7 นิ้ว แบตเตอรี่ 5,000 mAh", 14_999m, "demo-galaxy-a57.svg"),
            Product("OPPO Reno15 5G 256GB", "กล้องพอร์ตเทรต 50MP ชาร์จไว 80W", 16_990m, "demo-reno15.svg")),
        Category("ipad", "ไอแพด", 0.015m, 24, 2,
            Product("iPad (A16) Wi-Fi 128GB", "ไอแพดหน้าจอ 11 นิ้ว ชิป A16", 12_900m, "demo-ipad-a16.svg"),
            Product("iPad Air 11 นิ้ว M3 Wi-Fi 128GB", "ชิป M3 รองรับ Apple Pencil Pro", 21_900m, "demo-ipad-air-m3.svg")),
        Category("electrical-appliance", "เครื่องใช้ไฟฟ้า", 0.0125m, 18, 3,
            Product("ตู้เย็น 2 ประตู Samsung 13.8 คิว", "ระบบ Inverter ประหยัดไฟเบอร์ 5", 15_990m, "demo-fridge-samsung.svg"),
            Product("เครื่องซักผ้าฝาหน้า LG 10 กก.", "มอเตอร์ Inverter Direct Drive", 14_990m, "demo-washer-lg.svg"),
            Product("สมาร์ททีวี Sony 55 นิ้ว 4K", "Google TV ภาพคมชัดระดับ 4K HDR", 24_990m, "demo-tv-sony-55.svg")),
        Category("furniture", "เฟอร์นิเจอร์", 0.0125m, 18, 4,
            Product("โซฟาผ้า 3 ที่นั่ง", "ผ้ากันน้ำ โครงไม้จริง สีเทา", 12_900m, "demo-sofa-3-seat.svg"),
            Product("เตียงนอน 6 ฟุต พร้อมที่นอน", "ที่นอนพ็อกเก็ตสปริง หนา 10 นิ้ว", 18_500m, "demo-bed-6ft.svg"),
            Product("ตู้เสื้อผ้า 4 บานประตู", "กว้าง 160 ซม. พร้อมกระจกเงา", 8_990m, "demo-wardrobe-4-door.svg")),
        Category("electric-motorcycle", "มอเตอร์ไซค์ไฟฟ้า", 0.010m, 24, 5,
            Product("Yadea E8S Pro", "ความเร็วสูงสุด 75 กม./ชม. วิ่งได้ 100 กม. ต่อการชาร์จ", 42_900m, "demo-yadea-e8s-pro.svg"),
            Product("NIU NQi Sport", "แบตเตอรี่ลิเธียม ถอดชาร์จได้ เชื่อมต่อแอป", 55_900m, "demo-niu-nqi-sport.svg")),
    ];

    private static IEnumerable<Branch> Branches() =>
    [
        new() { Code = "bkk-ladprao", Name = "สาขาลาดพร้าว", Address = "123 ถนนลาดพร้าว แขวงจอมพล เขตจตุจักร กรุงเทพมหานคร 10900", Province = "กรุงเทพมหานคร" },
        new() { Code = "bkk-bangna", Name = "สาขาบางนา", Address = "45 ถนนบางนา-ตราด แขวงบางนา เขตบางนา กรุงเทพมหานคร 10260", Province = "กรุงเทพมหานคร" },
        new() { Code = "nonthaburi", Name = "สาขานนทบุรี", Address = "88 ถนนรัตนาธิเบศร์ ตำบลบางกระสอ อำเภอเมืองนนทบุรี นนทบุรี 11000", Province = "นนทบุรี" },
        new() { Code = "chiang-mai", Name = "สาขาเชียงใหม่", Address = "9 ถนนห้วยแก้ว ตำบลสุเทพ อำเภอเมืองเชียงใหม่ เชียงใหม่ 50200", Province = "เชียงใหม่" },
    ];

    private static ProductCategory Category(
        string code, string name, decimal flatRate, int longestTerm, int sortOrder, params Product[] products) =>
        new()
        {
            Code = code,
            Name = name,
            FlatRate = flatRate,
            LongestTerm = longestTerm,
            SortOrder = sortOrder,
            Products = [.. products],
        };

    // Image IDs refer to the demo photos eshop-node-api publishes at startup (services/eshop-node-api/demo-images).
    private static Product Product(string name, string description, decimal price, string imageId) =>
        new() { Name = name, Description = description, Price = price, ImageId = imageId };
}
