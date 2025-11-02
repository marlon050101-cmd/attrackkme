-- Simple script to add attendance records to existing students
-- This will make them appear as "at risk" in the guidance dashboard

-- First, let's see what students we have
SELECT 'Current students:' as Info;
SELECT StudentId, FullName, GradeLevel, Section FROM student WHERE IsActive = true LIMIT 5;

-- Add attendance records to make students appear as "at risk"
-- We'll add 3+ absences to the first few students

-- Get the first student ID
SET @student1 = (SELECT StudentId FROM student WHERE IsActive = true LIMIT 1);
SET @student2 = (SELECT StudentId FROM student WHERE IsActive = true LIMIT 1 OFFSET 1);
SET @student3 = (SELECT StudentId FROM student WHERE IsActive = true LIMIT 1 OFFSET 2);

-- Add attendance records for student 1 (3 absences)
INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
VALUES 
(CONCAT(@student1, '-att-001'), @student1, '2024-01-15', '08:00:00', '17:00:00', 'Present', 'On time'),
(CONCAT(@student1, '-att-002'), @student1, '2024-01-16', NULL, NULL, 'Absent', 'No show'),
(CONCAT(@student1, '-att-003'), @student1, '2024-01-17', NULL, NULL, 'Absent', 'Sick'),
(CONCAT(@student1, '-att-004'), @student1, '2024-01-18', NULL, NULL, 'Absent', 'Family emergency'),
(CONCAT(@student1, '-att-005'), @student1, '2024-01-19', '08:00:00', '17:00:00', 'Present', 'On time');

-- Add attendance records for student 2 (3 absences)
INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
VALUES 
(CONCAT(@student2, '-att-001'), @student2, '2024-01-15', '08:00:00', '17:00:00', 'Present', 'On time'),
(CONCAT(@student2, '-att-002'), @student2, '2024-01-16', NULL, NULL, 'Absent', 'No show'),
(CONCAT(@student2, '-att-003'), @student2, '2024-01-17', NULL, NULL, 'Absent', 'Sick'),
(CONCAT(@student2, '-att-004'), @student2, '2024-01-18', NULL, NULL, 'Absent', 'Personal issue'),
(CONCAT(@student2, '-att-005'), @student2, '2024-01-19', '08:00:00', '17:00:00', 'Present', 'On time');

-- Add attendance records for student 3 (4 absences)
INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
VALUES 
(CONCAT(@student3, '-att-001'), @student3, '2024-01-15', NULL, NULL, 'Absent', 'No show'),
(CONCAT(@student3, '-att-002'), @student3, '2024-01-16', '08:00:00', '17:00:00', 'Present', 'On time'),
(CONCAT(@student3, '-att-003'), @student3, '2024-01-17', NULL, NULL, 'Absent', 'Sick'),
(CONCAT(@student3, '-att-004'), @student3, '2024-01-18', NULL, NULL, 'Absent', 'Transportation problem'),
(CONCAT(@student3, '-att-005'), @student3, '2024-01-19', NULL, NULL, 'Absent', 'Family emergency');

-- Verify the results
SELECT 'Attendance records added!' as Status;

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
