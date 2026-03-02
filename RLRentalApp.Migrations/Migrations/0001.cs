using FluentMigrator;
using System;
using System.Collections.Generic;
using System.Text;

namespace Database.Migrations;

[Tags(TagNames.Rental)]
[Migration(0001)]
public class _0001_Schema01 : Migration
{
    public override void Down()
    {
        //No down. Drop db
    }


    public override void Up()
    {
  
        
        Execute.Script(@"Migrations\Scripts\0001.sql");

    }
}
