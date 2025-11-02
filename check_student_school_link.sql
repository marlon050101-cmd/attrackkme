-- Check if students are properly linked to guidance counselor's school
-- First, get the guidance counselor's school ID
SELECT 'Guidance Counselor School ID:' as Status;
SELECT t.SchoolId, s.SchoolName
FROM user u
INNER JOIN teacher t ON u.TeacherId = t.TeacherId
INNER JOIN school s ON t.SchoolId = s.SchoolId
WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777' 
AND u.UserType = 'GuidanceCounselor' 
AND u.IsActive = true;

-- Check students in that school
SELECT 'Students in Guidance Counselor School:' as Status;
SELECT s.StudentId, s.FullName, s.SchoolId, s.IsActive
FROM student s
INNER JOIN teacher t ON s.SchoolId = t.SchoolId
INNER JOIN user u ON t.TeacherId = u.TeacherId
WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777'
AND s.IsActive = true;

-- Check attendance records for students in that school
SELECT 'Attendance Records for Students in School:' as Status;
SELECT da.StudentId, s.FullName, da.Date, da.Status, da.Remarks
FROM daily_attendance da
INNER JOIN student s ON da.StudentId = s.StudentId
INNER JOIN teacher t ON s.SchoolId = t.SchoolId
INNER JOIN user u ON t.TeacherId = u.TeacherId
WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777'
AND s.IsActive = true
ORDER BY da.Date DESC;

-- Check students with 3+ absences in the last 30 days
SELECT 'Students with 3+ Absences (Last 30 Days):' as Status;
SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section,
       COUNT(da.AttendanceId) as TotalDays,
       SUM(CASE WHEN da.Status = 'Absent' THEN 1 ELSE 0 END) as AbsentDays
FROM student s
LEFT JOIN daily_attendance da ON s.StudentId = da.StudentId 
    AND da.Date >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
INNER JOIN teacher t ON s.SchoolId = t.SchoolId
INNER JOIN user u ON t.TeacherId = u.TeacherId
WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777'
AND s.IsActive = true
GROUP BY s.StudentId, s.FullName, s.GradeLevel, s.Section
HAVING AbsentDays >= 3
ORDER BY AbsentDays DESC;
