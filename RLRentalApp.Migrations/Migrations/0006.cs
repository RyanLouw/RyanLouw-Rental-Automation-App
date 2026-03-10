using FluentMigrator;

namespace Database.Migrations;

[Tags(TagNames.Rental)]
[Migration(0006)]
public class _0006_ResetToSingleDefaultPropertyTenant : Migration
{
    public override void Down()
    {
        // No down. Drop db
    }

    public override void Up()
    {
        Execute.Script(@"Migrations\Scripts\0006.sql");
    }
}
