-- Fix student school linking to match guidance counselor's school
-- First, get the guidance counselor's school ID
SET @guidance_school = (
    SELECT t.SchoolId 
    FROM user u
    INNER JOIN teacher t ON u.TeacherId = t.TeacherId
    WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777' 
    AND u.UserType = 'GuidanceCounselor' 
    AND u.IsActive = true
);

SELECT 'Guidance Counselor School ID:' as Status, @guidance_school as SchoolId;

-- Update students to be in the same school as guidance counselor
UPDATE student 
SET SchoolId = @guidance_school 
WHERE StudentId IN (
    'test-student-001', 
    'test-student-002', 
    'test-student-003',
    '67849d0e-b5e2-4336-ad4d-4a32c10f9bf8',
    '7adb8e32-2efa-4411-9442-e44deae9b96c'
);

-- Verify the update
SELECT 'Updated Students:' as Status;
SELECT s.StudentId, s.FullName, s.SchoolId, sch.SchoolName
FROM student s
LEFT JOIN school sch ON s.SchoolId = sch.SchoolId
WHERE s.StudentId IN (
    'test-student-001', 
    'test-student-002', 
    'test-student-003',
    '67849d0e-b5e2-4336-ad4d-4a32c10f9bf8',
    '7adb8e32-2efa-4411-9442-e44deae9b96c'
);

-- Test the attendance query that the guidance service uses
SELECT 'Students with 3+ Absences (Testing Query):' as Status;
SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender,
       COUNT(da.AttendanceId) as TotalDays,
       SUM(CASE WHEN da.Status = 'Present' THEN 1 ELSE 0 END) as PresentDays,
       SUM(CASE WHEN da.Status = 'Absent' THEN 1 ELSE 0 END) as AbsentDays,
       SUM(CASE WHEN da.Status = 'Late' THEN 1 ELSE 0 END) as LateDays
FROM student s
LEFT JOIN daily_attendance da ON s.StudentId = da.StudentId 
    AND da.Date >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
WHERE s.SchoolId = @guidance_school 
AND s.IsActive = true
GROUP BY s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender
HAVING AbsentDays >= 3
ORDER BY AbsentDays DESC, s.GradeLevel, s.Section, s.FullName;
