using FluentMigrator;

namespace Database.Migrations;

[Tags(TagNames.Rental)]
[Migration(0010)]
public class _0010_AddManualStatementEntry : Migration
{
    public override void Down()
    {
        // No down. Drop db
    }

    public override void Up()
    {
        Execute.Script(@"Migrations\Scripts\0010.sql");
    }
}
