-- Debug teacher-student relationship
SELECT '=== TEACHER-STUDENT DEBUG ===' as Status;

-- 1. Check if teacher exists
SELECT '1. Teacher Info:' as Step;
SELECT u.UserId, u.Username, u.TeacherId, t.TeacherId as ActualTeacherId, t.FullName, t.SchoolId, t.Gradelvl, t.Section
FROM user u
LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777';

-- 2. Check all students in database
SELECT '2. All Students:' as Step;
SELECT StudentId, FullName, GradeLevel, Section, SchoolId, IsActive
FROM student 
WHERE IsActive = 1
LIMIT 5;

-- 3. Check students in same school as teacher
SELECT '3. Students in Teacher School:' as Step;
SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.SchoolId
FROM student s
WHERE s.SchoolId = (
    SELECT t.SchoolId 
    FROM user u
    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
    WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777'
)
AND s.IsActive = 1;

-- 4. Check students with same grade as teacher
SELECT '4. Students in Same Grade:' as Step;
SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.SchoolId
FROM student s
WHERE s.SchoolId = (
    SELECT t.SchoolId 
    FROM user u
    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
    WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777'
)
AND s.GradeLevel = (
    SELECT t.Gradelvl 
    FROM user u
    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
    WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777'
)
AND s.IsActive = 1;

