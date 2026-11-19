IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'loyverse')
BEGIN
    EXEC('CREATE SCHEMA loyverse');
END
GO
