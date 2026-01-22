-- Creates the new table that supports multiple schedule events per day.
-- Safe to run multiple times.

IF OBJECT_ID('dbo.EmployeeScheduleBlocks', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmployeeScheduleBlocks
    (
        ScheduleBlockId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_EmployeeScheduleBlocks PRIMARY KEY,

        EmployeeId INT NOT NULL
            CONSTRAINT FK_EmployeeScheduleBlocks_Employees
            REFERENCES dbo.Employees(EmployeeId),

        ScheduleDate DATE NOT NULL,

        TypeId INT NOT NULL
            CONSTRAINT FK_EmployeeScheduleBlocks_AvailabilityTypes
            REFERENCES dbo.AvailabilityTypes(TypeId),

        StartTime TIME(0) NULL,
        EndTime   TIME(0) NULL,

        CustomerName NVARCHAR(200) NULL,
        OutActivity  NVARCHAR(200) NULL,
        Note         NVARCHAR(MAX) NULL,

        UpdatedByEmployeeId INT NOT NULL,
        UpdatedAt DATETIME2 NOT NULL
            CONSTRAINT DF_EmployeeScheduleBlocks_UpdatedAt DEFAULT (SYSUTCDATETIME())
    );

    CREATE INDEX IX_EmployeeScheduleBlocks_EmployeeDate
        ON dbo.EmployeeScheduleBlocks(EmployeeId, ScheduleDate);
END
GO

-- Optional one-time migration from the old single-row-per-day table.
-- It will migrate only rows that don't already exist in the new table.
IF OBJECT_ID('dbo.EmployeeSchedules', 'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.EmployeeScheduleBlocks
    (
        EmployeeId,
        ScheduleDate,
        TypeId,
        StartTime,
        EndTime,
        CustomerName,
        OutActivity,
        Note,
        UpdatedByEmployeeId,
        UpdatedAt
    )
    SELECT
        s.EmployeeId,
        s.ScheduleDate,
        s.TypeId,
        s.WindowStartTime,
        s.WindowEndTime,
        s.CustomerName,
        s.OutActivity,
        s.Note,
        s.UpdatedByEmployeeId,
        s.UpdatedAt
    FROM dbo.EmployeeSchedules s
    WHERE NOT EXISTS (
        SELECT 1
        FROM dbo.EmployeeScheduleBlocks b
        WHERE b.EmployeeId = s.EmployeeId
          AND b.ScheduleDate = s.ScheduleDate
    );
END
GO

-- Optional: if you are confident everything works, you can drop the old table later.
-- DROP TABLE dbo.EmployeeSchedules;
