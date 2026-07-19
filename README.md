# RyanLouw-Rental-Automation-App

## One-shot database migration script

If you want one file to run now instead of opening all 10 migration files, generate a combined SQL script from the existing ordered migration scripts and run that single file with `psql`.

> ⚠️ Important: migration `0009.sql` intentionally clears the demo/test rental data so you can start entering real property and renter data. Do not run the full script against a database that contains rental data you want to keep.

### macOS / Linux / Git Bash

```bash
# Run from the repository root.
set -euo pipefail

mkdir -p build
OUTPUT_FILE="build/full-rental-db-migration.sql"
: > "$OUTPUT_FILE"

for file in RLRentalApp.Migrations/Migrations/Scripts/*.sql; do
  {
    echo ""
    echo "-- ============================================================"
    echo "-- $file"
    echo "-- ============================================================"
    cat "$file"
    echo ""
  } >> "$OUTPUT_FILE"
done

echo "Created $OUTPUT_FILE"

# Apply the one generated file. Replace the connection string with your database details.
psql "Host=localhost;Port=5432;Database=rentaldb;Username=postgres;Password=your-password" -v ON_ERROR_STOP=1 -f "$OUTPUT_FILE"
```

### Windows PowerShell

```powershell
# Run from the repository root.
$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path build | Out-Null
$outputFile = "build/full-rental-db-migration.sql"
Set-Content -Path $outputFile -Value ""

Get-ChildItem "RLRentalApp.Migrations/Migrations/Scripts/*.sql" | Sort-Object Name | ForEach-Object {
    Add-Content -Path $outputFile -Value ""
    Add-Content -Path $outputFile -Value "-- ============================================================"
    Add-Content -Path $outputFile -Value "-- $($_.FullName)"
    Add-Content -Path $outputFile -Value "-- ============================================================"
    Get-Content $_.FullName | Add-Content -Path $outputFile
    Add-Content -Path $outputFile -Value ""
}

Write-Host "Created $outputFile"

# Apply the one generated file. Replace the connection string with your database details.
psql "Host=localhost;Port=5432;Database=rentaldb;Username=postgres;Password=your-password" -v ON_ERROR_STOP=1 -f $outputFile
```

If you want to keep the demo/test data, use the normal migration runner instead of this full reset-style script, or remove the `0009.sql` section from `build/full-rental-db-migration.sql` before running it.


## Going to a live database / production

Use PostgreSQL for the live database and keep production secrets outside git.

### 1. Create the live PostgreSQL database

Create a hosted PostgreSQL database with SSL enabled. Keep these values from your provider:

- Host
- Port, normally `5432`
- Database name, for example `rentaldb`
- Username
- Password
- SSL requirement

Before changing an existing live database, take a backup first.

### 2. Configure the live connection string

Set the production connection string as an environment variable on the server or hosting platform:

```bash
ConnectionStrings__rentaldb="Host=your-db-host;Port=5432;Database=rentaldb;Username=your-db-user;Password=your-db-password;SSL Mode=Require;Trust Server Certificate=true"
```

Do not put the live password in `appsettings.json`, `appsettings.Development.json`, or any committed file.

### 3. Configure live email secrets

Set the Gmail SMTP values as environment variables too:

```bash
GmailSmtp__Host="smtp.gmail.com"
GmailSmtp__Port="587"
GmailSmtp__EnableSsl="true"
GmailSmtp__Username="yourgmail@gmail.com"
GmailSmtp__AppPassword="your-16-char-app-password"
GmailSmtp__FromEmail="yourgmail@gmail.com"
GmailSmtp__FromDisplayName="MH & Sons Properties"
```

### 4. Run the rental database migrations once

For a brand-new live database, run the migration project against the live connection string:

```bash
ConnectionStrings__rentaldb="Host=your-db-host;Port=5432;Database=rentaldb;Username=your-db-user;Password=your-db-password;SSL Mode=Require;Trust Server Certificate=true" \
  dotnet run --project RLRentalApp.Migrations/RLRentalApp.Migrations.csproj
```

You can also use the one-shot SQL script above, but check `0009.sql` first because it truncates rental data. On a new empty live database that is fine; on a database with real data, do not run the truncation section.

### 5. Start the web app

Start `RLRentalApp.Web` with the same `ConnectionStrings__rentaldb` and `GmailSmtp__...` environment variables. When the web app starts, it automatically applies the ASP.NET Identity tables/migrations for login users.

```bash
ASPNETCORE_ENVIRONMENT="Production" \
ConnectionStrings__rentaldb="Host=your-db-host;Port=5432;Database=rentaldb;Username=your-db-user;Password=your-db-password;SSL Mode=Require;Trust Server Certificate=true" \
dotnet run --project RLRentalApp.Web/RLRentalApp.Web.csproj
```

### 6. Production checklist

- Keep SSL required for the database connection.
- Keep all passwords and app passwords in environment variables or a managed secret store.
- Run migrations before pointing users at the app.
- Confirm login works and change any default/admin password immediately.
- Send one test statement email to yourself before emailing tenants.
- Schedule automatic database backups with your hosting provider.
- Do not run the `0009.sql` truncation step after real rental data exists.

## Gmail SMTP setup (send emails to tenants)

Use the `GmailSmtp` section in `RLRentalApp.Web/appsettings.Development.json` (or environment variables in production).

```json
"GmailSmtp": {
  "Host": "smtp.gmail.com",
  "Port": 587,
  "EnableSsl": true,
  "Username": "yourgmail@gmail.com",
  "AppPassword": "your-gmail-app-password",
  "FromEmail": "yourgmail@gmail.com",
  "FromDisplayName": "RLRentalApp"
}
```

### Where each value comes from

- `Host`: keep as `smtp.gmail.com`.
- `Port`: use `587` for TLS (recommended).
- `EnableSsl`: keep as `true`.
- `Username`: your full Gmail address (example: `you@gmail.com`).
- `AppPassword`: a 16-character Google **App Password** (not your normal Gmail password).
- `FromEmail`: usually the same as `Username`.
- `FromDisplayName`: friendly sender name tenants see (example: `MH & Sons Properties`).

### How to get `AppPassword`

1. Sign in to the Gmail account you want to send from.
2. Open your Google Account Security page: `https://myaccount.google.com/security`.
3. Turn on **2-Step Verification** (required before App Passwords are available).
4. Go to **App passwords**: `https://myaccount.google.com/apppasswords`.
5. Choose app **Mail** (or Custom name like `RLRentalApp`) and generate.
6. Copy the generated 16-character password and place it in `AppPassword`.

### Important security note

Do **not** commit real credentials to git. Prefer local secrets or environment variables.

Example environment variables:

- `GmailSmtp__Host=smtp.gmail.com`
- `GmailSmtp__Port=587`
- `GmailSmtp__EnableSsl=true`
- `GmailSmtp__Username=yourgmail@gmail.com`
- `GmailSmtp__AppPassword=xxxxxxxxxxxxxxxx`
- `GmailSmtp__FromEmail=yourgmail@gmail.com`
- `GmailSmtp__FromDisplayName=MH & Sons Properties`


### Troubleshooting `5.7.0 Authentication Required`

If you see:

- `The SMTP server requires a secure connection or the client was not authenticated.`
- `5.7.0 Authentication Required`

Check these in order:

1. `AppPassword` must be a Google **App Password** (16 chars), not your Gmail login password.
2. **2-Step Verification** must be ON for that Google account.
3. `Username` should be the same mailbox you generated the App Password for.
4. `FromEmail` should match `Username` unless you configured **Send mail as** in Gmail settings.
5. If this is a Google Workspace account, SMTP auth may be blocked by admin policy.

Also note: when pasting the app password, remove spaces shown in Google's UI.


### Statement email behavior in dashboard

- Subject and body are auto-generated by the app.
- Subject format: `Statement - <property address> - <Month Year>`.
- Body text: `Please see attached statement for <Month Year>.`
- The tenant receives the generated statement PDF as an attachment.
- Before sending, the dashboard shows an email preview modal with the statement table and totals.


## Automatic late-payment interest and demand emails

Late-payment interest is not added by a scheduled background job. It is added immediately when you save newly detected payments from the dashboard bank-statement payment workflow. This applies to both the selected-property save and the all-renter payment save, because both use the same save-payment path.

### When late interest is added

The app adds late interest only after a payment is successfully saved as a new payment row. Duplicate payments are skipped first, so skipped duplicates do not create another interest charge or another email.

For each saved payment, late interest is added when all of these are true:

- The payment amount is more than zero.
- The payment date is after the 4th day of that same month. For example, a payment dated the 5th or later can be charged interest; a payment dated the 1st through the 4th will not be charged late interest.
- The tenant still had an outstanding balance before that payment was applied.
- The app can match the saved payment back to its database payment row.
- A late-interest or late-payment-letter row has not already been added for that same payment.

### How the interest and fee are calculated

The due date used by the calculation is the 4th of the payment month. The number of late days is the payment date minus that 4th-day due date.

- If the late payment settles the full amount owed, the app adds only the late interest:

  ```text
  balance owed before payment x days late / 365 x 23%
  ```

- If the late payment does not settle the full amount owed, the app adds:

  ```text
  balance owed before payment x days late / 365 x 23%
  + outstanding balance after payment x 30 / 365 x 23%
  + R200 late payment letter fee
  ```

The statement description stores the maths that was used, so the tenant statement shows how the interest was calculated. The R200 late-payment-letter fee is only added when the account is still not fully paid after the payment.

### When late-payment emails are sent

The late-rent demand email is sent immediately after the late interest has been added, during the same save-payment action. The email is sent only when the tenant has an email address saved. If sending the email fails, the payment and late charges stay saved; the app does not roll back the payment just because the email could not be sent.

The email subject is:

```text
Late rent - demand for payment - <payment date>
```

The email body includes the interest calculation, the interest amount, the optional R200 late-payment-letter line, the total late charges added, and the current balance that must be paid immediately.


## Safer secret storage (recommended)

Use one of these two approaches so passwords are never committed:

### Option 1 (best for local dev): .NET User Secrets

From the `RLRentalApp.Web` folder run:

```bash
dotnet user-secrets init
dotnet user-secrets set "GmailSmtp:Host" "smtp.gmail.com"
dotnet user-secrets set "GmailSmtp:Port" "587"
dotnet user-secrets set "GmailSmtp:EnableSsl" "true"
dotnet user-secrets set "GmailSmtp:Username" "yourgmail@gmail.com"
dotnet user-secrets set "GmailSmtp:AppPassword" "your-16-char-app-password"
dotnet user-secrets set "GmailSmtp:FromEmail" "yourgmail@gmail.com"
dotnet user-secrets set "GmailSmtp:FromDisplayName" "RLRentalApp"
```

### Option 2: local file override (gitignored)

This project also loads `appsettings.Local.json` automatically.
Create `RLRentalApp.Web/appsettings.Local.json` with your private values (do not commit it):

```json
{
  "GmailSmtp": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "EnableSsl": true,
    "Username": "yourgmail@gmail.com",
    "AppPassword": "your-16-char-app-password",
    "FromEmail": "yourgmail@gmail.com",
    "FromDisplayName": "RLRentalApp"
  }
}
```

For production, use environment variables or a managed secret store (Azure Key Vault / AWS Secrets Manager / etc.).

### Will this change `appsettings` or the demo?

- The app still reads `appsettings.json` and `appsettings.Development.json` exactly as before.
- `appsettings.Local.json` is **optional** and only overrides values if you create it locally.
- Demo/default behavior is unchanged unless you add local secrets or environment variables.

## Google Drive setup (save statement PDFs to your Drive)

This app uses **Sign in with Google** and asks for permission to create files in the Google Drive of the account you sign in with. It does not use a service-account JSON key.

1. In Google Cloud Console, create an **OAuth 2.0 Client ID** of type **Web application**.
2. Add your app callback URL: `https://your-domain/Account/GoogleLoginCallback` (for local development, use the exact local HTTPS URL shown by the app plus that path).
3. Enable the **Google Drive API**.
4. Create a folder in the Google Drive account you will use, and copy its folder ID from the URL.
5. Add the OAuth client values outside git in `RLRentalApp.Web/appsettings.Local.json`:

```json
{
  "GoogleDrive": {
    "Enabled": true,
    "ClientId": "your-oauth-client-id",
    "ClientSecret": "your-oauth-client-secret",
    "FolderId": "your-google-drive-folder-id"
  }
}
```

Then choose **Sign in with Google and connect Drive** on the login page. Sign in with the Google account that owns or can edit the selected folder.

### Test the connection

Use **Test Google Drive** at the top of the Property Dashboard. A successful test creates a timestamped folder and `connection-test.txt` in your configured Drive folder.
