using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eshop.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "image_id",
                table: "products",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Databases seeded before this migration (DemoData only seeds empty tables) get the demo photos too.
            migrationBuilder.Sql("""
                UPDATE products AS p SET image_id = demo.image_id
                FROM (VALUES
                    ('iPhone 17 128GB', 'demo-iphone-17.svg'),
                    ('Samsung Galaxy A57 5G 256GB', 'demo-galaxy-a57.svg'),
                    ('OPPO Reno15 5G 256GB', 'demo-reno15.svg'),
                    ('iPad (A16) Wi-Fi 128GB', 'demo-ipad-a16.svg'),
                    ('iPad Air 11 นิ้ว M3 Wi-Fi 128GB', 'demo-ipad-air-m3.svg'),
                    ('ตู้เย็น 2 ประตู Samsung 13.8 คิว', 'demo-fridge-samsung.svg'),
                    ('เครื่องซักผ้าฝาหน้า LG 10 กก.', 'demo-washer-lg.svg'),
                    ('สมาร์ททีวี Sony 55 นิ้ว 4K', 'demo-tv-sony-55.svg'),
                    ('โซฟาผ้า 3 ที่นั่ง', 'demo-sofa-3-seat.svg'),
                    ('เตียงนอน 6 ฟุต พร้อมที่นอน', 'demo-bed-6ft.svg'),
                    ('ตู้เสื้อผ้า 4 บานประตู', 'demo-wardrobe-4-door.svg'),
                    ('Yadea E8S Pro', 'demo-yadea-e8s-pro.svg'),
                    ('NIU NQi Sport', 'demo-niu-nqi-sport.svg')
                ) AS demo(name, image_id)
                WHERE p.name = demo.name AND p.image_id IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image_id",
                table: "products");
        }
    }
}
