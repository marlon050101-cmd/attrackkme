-- Update existing students with attendance records to show in guidance dashboard
-- This script adds attendance records to existing students to make them appear as "at risk"

-- First, let's see what students we have
SELECT 'Current students in database:' as Info;
SELECT StudentId, FullName, GradeLevel, Section, SchoolId FROM student LIMIT 10;

-- Add attendance records for existing students
-- We'll add records for the first few students to make them appear as "at risk" (3+ absences)

-- Get the first student and add attendance records
INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
SELECT 
    CONCAT('att-', s.StudentId, '-001') as AttendanceId,
    s.StudentId,
    '2024-01-15' as Date,
    '08:00:00' as TimeIn,
    '17:00:00' as TimeOut,
    'Present' as Status,
    'On time' as Remarks
FROM student s 
WHERE s.StudentId = (SELECT StudentId FROM student LIMIT 1);

INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
SELECT 
    CONCAT('att-', s.StudentId, '-002') as AttendanceId,
    s.StudentId,
    '2024-01-16' as Date,
    NULL as TimeIn,
    NULL as TimeOut,
    'Absent' as Status,
    'No show' as Remarks
FROM student s 
WHERE s.StudentId = (SELECT StudentId FROM student LIMIT 1);

INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
SELECT 
    CONCAT('att-', s.StudentId, '-003') as AttendanceId,
    s.StudentId,
    '2024-01-17' as Date,
    NULL as TimeIn,
    NULL as TimeOut,
    'Absent' as Status,
    'Sick' as Remarks
FROM student s 
WHERE s.StudentId = (SELECT StudentId FROM student LIMIT 1);

INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
SELECT 
    CONCAT('att-', s.StudentId, '-004') as AttendanceId,
    s.StudentId,
    '2024-01-18' as Date,
    NULL as TimeIn,
    NULL as TimeOut,
    'Absent' as Status,
    'Family emergency' as Remarks
FROM student s 
WHERE s.StudentId = (SELECT StudentId FROM student LIMIT 1);

INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
SELECT 
    CONCAT('att-', s.StudentId, '-005') as AttendanceId,
    s.StudentId,
    '2024-01-19' as Date,
    '08:00:00' as TimeIn,
    '17:00:00' as TimeOut,
    'Present' as Status,
    'On time' as Remarks
FROM student s 
WHERE s.StudentId = (SELECT StudentId FROM student LIMIT 1);

-- Add attendance for second student (if exists)
INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
SELECT 
    CONCAT('att-', s.StudentId, '-001') as AttendanceId,
    s.StudentId,
    '2024-01-15' as Date,
    '08:00:00' as TimeIn,
    '17:00:00' as TimeOut,
    'Present' as Status,
    'On time' as Remarks
FROM student s 
WHERE s.StudentId = (SELECT StudentId FROM student LIMIT 1 OFFSET 1);

INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
SELECT 
    CONCAT('att-', s.StudentId, '-002') as AttendanceId,
    s.StudentId,
    '2024-01-16' as Date,
    NULL as TimeIn,
    NULL as TimeOut,
    'Absent' as Status,
    'No show' as Remarks
FROM student s 
WHERE s.StudentId = (SELECT StudentId FROM student LIMIT 1 OFFSET 1);

INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
SELECT 
    CONCAT('att-', s.StudentId, '-003') as AttendanceId,
    s.StudentId,
    '2024-01-17' as Date,
    NULL as TimeIn,
    NULL as TimeOut,
    'Absent' as Status,
    'Sick' as Remarks
FROM student s 
WHERE s.StudentId = (SELECT StudentId FROM student LIMIT 1 OFFSET 1);

INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
SELECT 
    CONCAT('att-', s.StudentId, '-004') as AttendanceId,
    s.StudentId,
    '2024-01-18' as Date,
    NULL as TimeIn,
    NULL as TimeOut,
    'Absent' as Status,
    'Personal issue' as Remarks
FROM student s 
WHERE s.StudentId = (SELECT StudentId FROM student LIMIT 1 OFFSET 1);

INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
SELECT 
    CONCAT('att-', s.StudentId, '-005') as AttendanceId,
    s.StudentId,
    '2024-01-19' as Date,
    '08:00:00' as TimeIn,
    '17:00:00' as TimeOut,
    'Present' as Status,
    'On time' as Remarks
FROM student s 
WHERE s.StudentId = (SELECT StudentId FROM student LIMIT 1 OFFSET 1);

-- Add attendance for third student (if exists)
INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
SELECT 
    CONCAT('att-', s.StudentId, '-001') as AttendanceId,
    s.StudentId,
    '2024-01-15' as Date,
    NULL as TimeIn,
    NULL as TimeOut,
    'Absent' as Status,
    'No show' as Remarks
FROM student s 
WHERE s.StudentId = (SELECT StudentId FROM student LIMIT 1 OFFSET 2);

INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
SELECT 
    CONCAT('att-', s.StudentId, '-002') as AttendanceId,
    s.StudentId,
    '2024-01-16' as Date,
    '08:00:00' as TimeIn,
    '17:00:00' as TimeOut,
    'Present' as Status,
    'On time' as Remarks
FROM student s 
WHERE s.StudentId = (SELECT StudentId FROM student LIMIT 1 OFFSET 2);

INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
SELECT 
    CONCAT('att-', s.StudentId, '-003') as AttendanceId,
    s.StudentId,
    '2024-01-17' as Date,
    NULL as TimeIn,
    NULL as TimeOut,
    'Absent' as Status,
    'Sick' as Remarks
FROM student s 
WHERE s.StudentId = (SELECT StudentId FROM student LIMIT 1 OFFSET 2);

INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
SELECT 
    CONCAT('att-', s.StudentId, '-004') as AttendanceId,
    s.StudentId,
    '2024-01-18' as Date,
    NULL as TimeIn,
    NULL as TimeOut,
    'Absent' as Status,
    'Transportation problem' as Remarks
FROM student s 
WHERE s.StudentId = (SELECT StudentId FROM student LIMIT 1 OFFSET 2);

INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
SELECT 
    CONCAT('att-', s.StudentId, '-005') as AttendanceId,
    s.StudentId,
    '2024-01-19' as Date,
    NULL as TimeIn,
    NULL as TimeOut,
    'Absent' as Status,
    'Family emergency' as Remarks
FROM student s 
WHERE s.StudentId = (SELECT StudentId FROM student LIMIT 1 OFFSET 2);

-- Verify the results
SELECT 'Attendance records added successfully!' as Status;
SELECT 'Check students with 3+ absences:' as Info;

-- Show students with 3+ absences
SELECT 
    s.StudentId,
    s.FullName,
    s.GradeLevel,
    s.Section,
    COUNT(da.AttendanceId) as TotalDays,
    SUM(CASE WHEN da.Status = 'Present' THEN 1 ELSE 0 END) as PresentDays,
    SUM(CASE WHEN da.Status = 'Absent' THEN 1 ELSE 0 END) as AbsentDays,
    SUM(CASE WHEN da.Status = 'Late' THEN 1 ELSE 0 END) as LateDays
FROM student s
LEFT JOIN daily_attendance da ON s.StudentId = da.StudentId 
    AND da.Date >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
WHERE s.IsActive = true
GROUP BY s.StudentId, s.FullName, s.GradeLevel, s.Section
HAVING AbsentDays >= 3
ORDER BY AbsentDays DESC;
