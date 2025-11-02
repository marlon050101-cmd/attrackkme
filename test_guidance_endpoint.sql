-- Test the exact query that the guidance controller uses
-- This simulates the GetGuidanceCounselorSchoolIdAsync method

SELECT 'Testing Guidance Counselor School ID Query:' as Status;

SELECT t.SchoolId 
FROM user u
INNER JOIN teacher t ON u.TeacherId = t.TeacherId
WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777' 
AND u.UserType = 'GuidanceCounselor' 
AND u.IsActive = true;

-- If the above returns a school ID, test the students query
SELECT 'Testing Students Query:' as Status;
SELECT s.StudentId, s.FullName, s.Email, s.GradeLevel, s.Section, s.Strand, 
       s.ParentsNumber, s.Gender, s.IsActive, s.CreatedAt, s.UpdatedAt
FROM student s
WHERE s.SchoolId = (
    SELECT t.SchoolId 
    FROM user u
    INNER JOIN teacher t ON u.TeacherId = t.TeacherId
    WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777' 
    AND u.UserType = 'GuidanceCounselor' 
    AND u.IsActive = true
) 
AND s.IsActive = true
ORDER BY s.GradeLevel, s.Section, s.FullName;

-- Test the attendance summary query
SELECT 'Testing Attendance Summary Query:' as Status;
SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender,
       COUNT(da.AttendanceId) as TotalDays,
       SUM(CASE WHEN da.Status = 'Present' THEN 1 ELSE 0 END) as PresentDays,
       SUM(CASE WHEN da.Status = 'Absent' THEN 1 ELSE 0 END) as AbsentDays,
       SUM(CASE WHEN da.Status = 'Late' THEN 1 ELSE 0 END) as LateDays
FROM student s
LEFT JOIN daily_attendance da ON s.StudentId = da.StudentId 
    AND da.Date >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
WHERE s.SchoolId = (
    SELECT t.SchoolId 
    FROM user u
    INNER JOIN teacher t ON u.TeacherId = t.TeacherId
    WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777' 
    AND u.UserType = 'GuidanceCounselor' 
    AND u.IsActive = true
) 
AND s.IsActive = true
GROUP BY s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender
HAVING AbsentDays >= 3
ORDER BY AbsentDays DESC, s.GradeLevel, s.Section, s.FullName;
