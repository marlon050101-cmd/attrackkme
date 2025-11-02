-- Debug script to check guidance counselor and school data
-- Check if the guidance counselor user exists
SELECT 'Guidance Counselor User:' as Status;
SELECT u.UserId, u.Username, u.UserType, u.TeacherId, u.IsActive
FROM user u 
WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777';

-- Check if the teacher record exists for this guidance counselor
SELECT 'Guidance Counselor Teacher Record:' as Status;
SELECT t.TeacherId, t.FullName, t.SchoolId, t.Gradelvl, t.Section, t.Strand
FROM teacher t
INNER JOIN user u ON t.TeacherId = u.TeacherId
WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777';

-- Check if the school exists
SELECT 'School Information:' as Status;
SELECT s.SchoolId, s.SchoolName
FROM school s
INNER JOIN teacher t ON s.SchoolId = t.SchoolId
INNER JOIN user u ON t.TeacherId = u.TeacherId
WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777';

-- Check students in the school
SELECT 'Students in School:' as Status;
SELECT s.StudentId, s.FullName, s.SchoolId, s.IsActive
FROM student s
INNER JOIN teacher t ON s.SchoolId = t.SchoolId
INNER JOIN user u ON t.TeacherId = u.TeacherId
WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777'
AND s.IsActive = true;

-- Check attendance records
SELECT 'Attendance Records:' as Status;
SELECT da.AttendanceId, da.StudentId, da.Date, da.Status, da.Remarks
FROM daily_attendance da
INNER JOIN student s ON da.StudentId = s.StudentId
INNER JOIN teacher t ON s.SchoolId = t.SchoolId
INNER JOIN user u ON t.TeacherId = u.TeacherId
WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777'
ORDER BY da.Date DESC
LIMIT 10;
