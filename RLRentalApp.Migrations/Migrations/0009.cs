using FluentMigrator;

namespace Database.Migrations;

[Tags(TagNames.Rental)]
[Migration(0009)]
public class _0009_ClearRentalDataForRealStart : Migration
{
    public override void Down()
    {
        // No down. This intentionally clears seeded/test rental data so the app can start with real data.
    }

    public override void Up()
    {
        Execute.Script(@"Migrations\Scripts\0009.sql");
    }
}
