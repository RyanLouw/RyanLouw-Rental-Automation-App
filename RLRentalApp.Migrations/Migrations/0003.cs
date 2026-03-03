using FluentMigrator;

namespace Database.Migrations;

[Tags(TagNames.Rental)]
[Migration(0003)]
public class _0003_SeedDummyData : Migration
{
    public override void Down()
    {
        // No down. Drop db
    }

    public override void Up()
    {
        Execute.Script(@"Migrations\Scripts\0003.sql");
    }
}
