using FluentMigrator;

namespace Database.Migrations;

[Tags(TagNames.Rental)]
[Migration(0008)]
public class _0008_SeedPaymentMatchingDemoProperties : Migration
{
    public override void Down()
    {
        // No down. Drop db
    }

    public override void Up()
    {
        Execute.Script(@"Migrations\Scripts\0008.sql");
    }
}
