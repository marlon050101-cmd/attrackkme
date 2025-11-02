-- Check teacher data to see GradeLevel and Section
SELECT 'Teacher Data Check:' as Status;

-- Check if teacher exists
SELECT u.UserId, u.Username, u.Email, u.UserType, u.TeacherId, t.FullName, t.Gradelvl, t.Section, t.SchoolId
FROM user u
LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777';

-- Check students in the same school
SELECT 'Students in School:' as Status;
SELECT StudentId, FullName, GradeLevel, Section, SchoolId
FROM student 
WHERE SchoolId = (
    SELECT t.SchoolId 
    FROM user u
    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
    WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777'
)
AND IsActive = 1;
