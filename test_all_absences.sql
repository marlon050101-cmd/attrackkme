-- Test query to find all students with 3+ absences (no date filter)
-- Get guidance counselor's school
SET @guidance_school = (
    SELECT t.SchoolId 
    FROM user u
    INNER JOIN teacher t ON u.TeacherId = t.TeacherId
    WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777' 
    AND u.UserType = 'GuidanceCounselor' 
    AND u.IsActive = true
);

SELECT 'All Students with 3+ Absences (No Date Filter):' as Status;
SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender,
       COUNT(da.AttendanceId) as TotalDays,
       SUM(CASE WHEN da.Status = 'Present' THEN 1 ELSE 0 END) as PresentDays,
       SUM(CASE WHEN da.Status = 'Absent' THEN 1 ELSE 0 END) as AbsentDays,
       SUM(CASE WHEN da.Status = 'Late' THEN 1 ELSE 0 END) as LateDays
FROM student s
LEFT JOIN daily_attendance da ON s.StudentId = da.StudentId
WHERE s.SchoolId = @guidance_school 
AND s.IsActive = true
GROUP BY s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender
HAVING AbsentDays >= 3
ORDER BY AbsentDays DESC, s.GradeLevel, s.Section, s.FullName;
