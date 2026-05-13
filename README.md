# Mini ORM 

## 1) PostgreSQL Setup

1. Install PostgreSQL (and pgAdmin if GUI is needed)
2. Set up a password when prompted(need to use later)
3. Install properly
4. Check if PostgreSQL service is running (needs to run)


## 2) Set `MINIORM_CONN` Environment Variable

This project reads DB connection from environment variable.  

In powershell (in current terminal):
$env:MINIORM_CONN="Host=localhost;Port=5432;Database=miniorm;Username=postgres;Password=my_password"


## 3) Run Migrations

From solution root/MiniOrm.Migrations:

In powershell:
dotnet run -- migrations add InitSchema
dotnet run -- migrations apply
dotnet run -- migrations list
dotnet run -- migrations rollback


Migration SQL files are created in:
`MiniOrm.Migrations/Migrations`


## 4) Run the Demo

From solution root:

In powershell:
dotnet run --project .\MiniOrm


Demo flow:
1. Create `AppDbContext` using `MINIORM_CONN`
2. Insert product
3. Find product by id
4. Update product
5. Get all products
6. Delete product

## 5) Type Mapping 

`TypeMapper` converts C# types to PostgreSQL types.

List:
- `int (Primary Key)` -> `SERIAL`
- `int` -> `INTEGER`
- `long` -> `BIGINT`
- `float` -> `REAL`
- `double` -> `DOUBLE PRECISION`
- `decimal` -> `NUMERIC`
- `bool` -> `BOOLEAN`
- `DateTime` -> `TIMESTAMP`
- `Guid` -> `UUID`
- `string` -> `TEXT`


Nullable value types are handled using:

`Nullable.GetUnderlyingType(type)`

## 6) Attribute Filtering

Only properties with `[Column]` or `[PrimaryKey]` are mapped.

This means:
- normal mapped fields go to DB columns
- navigation/unwanted properties are ignored



## 7) Build 

In powershell:
dotnet build .\MiniOrmSolution.sln


