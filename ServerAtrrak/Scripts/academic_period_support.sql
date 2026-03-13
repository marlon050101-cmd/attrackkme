-- Migration script for Academic Period Support
-- Adds support for tracking School Years and Semesters

-- 1. Create academic_period table
CREATE TABLE IF NOT EXISTS academic_period (
    PeriodId VARCHAR(36) PRIMARY KEY,
    SchoolId VARCHAR(36) NOT NULL,
    SchoolYear VARCHAR(20) NOT NULL, -- e.g., "2023-2024"
    Semester VARCHAR(20) NOT NULL,   -- e.g., "1st Semester", "Regular"
    IsActive BOOLEAN DEFAULT FALSE,  -- Only one active per school
    StartDate DATE,
    EndDate DATE,
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_school_active (SchoolId, IsActive)
);

-- 2. Add PeriodId to existing tables
-- Using temporary procedures to avoid errors if columns already exist (safe for re-running)

DELIMITER //

CREATE PROCEDURE AddPeriodIdIfMissing()
BEGIN
    -- teachersubject
    IF NOT EXISTS (SELECT * FROM information_schema.columns WHERE table_name = 'teachersubject' AND column_name = 'PeriodId' AND table_schema = DATABASE()) THEN
        ALTER TABLE teachersubject ADD COLUMN PeriodId VARCHAR(36) NULL;
        ALTER TABLE teachersubject ADD INDEX idx_ts_period (PeriodId);
    END IF;

    -- class_offering
    IF NOT EXISTS (SELECT * FROM information_schema.columns WHERE table_name = 'class_offering' AND column_name = 'PeriodId' AND table_schema = DATABASE()) THEN
        ALTER TABLE class_offering ADD COLUMN PeriodId VARCHAR(36) NULL;
        ALTER TABLE class_offering ADD INDEX idx_co_period (PeriodId);
    END IF;

    -- student_daily_summary
    IF NOT EXISTS (SELECT * FROM information_schema.columns WHERE table_name = 'student_daily_summary' AND column_name = 'PeriodId' AND table_schema = DATABASE()) THEN
        ALTER TABLE student_daily_summary ADD COLUMN PeriodId VARCHAR(36) NULL;
        ALTER TABLE student_daily_summary ADD INDEX idx_sds_period (PeriodId);
    END IF;

    -- subject_attendance
    IF NOT EXISTS (SELECT * FROM information_schema.columns WHERE table_name = 'subject_attendance' AND column_name = 'PeriodId' AND table_schema = DATABASE()) THEN
        ALTER TABLE subject_attendance ADD COLUMN PeriodId VARCHAR(36) NULL;
        ALTER TABLE subject_attendance ADD INDEX idx_sa_period (PeriodId);
    END IF;

    -- guidance_cases
    IF NOT EXISTS (SELECT * FROM information_schema.columns WHERE table_name = 'guidance_cases' AND column_name = 'PeriodId' AND table_schema = DATABASE()) THEN
        ALTER TABLE guidance_cases ADD COLUMN PeriodId VARCHAR(36) NULL;
        ALTER TABLE guidance_cases ADD INDEX idx_gc_period (PeriodId);
    END IF;
END //

DELIMITER ;

CALL AddPeriodIdIfMissing();
DROP PROCEDURE AddPeriodIdIfMissing();

-- 3. Initialize a default period for existing data if a school exists
INSERT INTO academic_period (PeriodId, SchoolId, SchoolYear, Semester, IsActive, StartDate, EndDate)
SELECT 
    UUID(), 
    SchoolId, 
    '2023-2024', 
    'Regular', 
    TRUE, 
    CURDATE(), 
    DATE_ADD(CURDATE(), INTERVAL 1 YEAR)
FROM school 
WHERE SchoolId NOT IN (SELECT DISTINCT SchoolId FROM academic_period)
LIMIT 1;

-- 4. Backfill existing data with the newly created period (optional but good for consistency)
SET @DefaultPeriod = (SELECT PeriodId FROM academic_period WHERE IsActive = TRUE LIMIT 1);

UPDATE teachersubject SET PeriodId = @DefaultPeriod WHERE PeriodId IS NULL;
UPDATE class_offering SET PeriodId = @DefaultPeriod WHERE PeriodId IS NULL;
UPDATE student_daily_summary SET PeriodId = @DefaultPeriod WHERE PeriodId IS NULL;
UPDATE subject_attendance SET PeriodId = @DefaultPeriod WHERE PeriodId IS NULL;
UPDATE guidance_cases SET PeriodId = @DefaultPeriod WHERE PeriodId IS NULL;
