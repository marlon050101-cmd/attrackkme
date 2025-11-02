-- Add test data for guidance dashboard
-- This script adds a test student with attendance records

-- First, let's add a test school for Koronadal (if it doesn't exist)
INSERT IGNORE INTO school (SchoolId, SchoolName, Region, Division, District, SchoolAddress)
VALUES (
    'test-school-koronadal-001',
    'Koronadal National High School',
    'Region XII',
    'South Cotabato Division',
    'Koronadal City',
    'Koronadal City, South Cotabato'
);

-- Add a test guidance counselor
INSERT IGNORE INTO teacher (TeacherId, FullName, Email, SchoolId, Gradelvl, Section, Strand)
VALUES (
    'test-guidance-001',
    'MARIA SANTOS',
    'maria.santos@koronadal.edu.ph',
    'test-school-koronadal-001',
    0,
    NULL,
    NULL
);

-- Add a test user for the guidance counselor
INSERT IGNORE INTO user (UserId, Username, Email, Password, UserType, IsActive, CreatedAt, UpdatedAt, TeacherId)
VALUES (
    'test-guidance-user-001',
    'guidance_maria',
    'maria.santos@koronadal.edu.ph',
    'password123',
    'GuidanceCounselor',
    true,
    NOW(),
    NOW(),
    'test-guidance-001'
);

-- Add test students (3 students only)
INSERT IGNORE INTO student (StudentId, FullName, Email, GradeLevel, Section, Strand, SchoolId, ParentsNumber, Gender, IsActive)
VALUES 
('test-student-001', 'JUAN DELACRUZ', 'juan.delacruz@student.koronadal.edu.ph', 11, 'HUMSS 2', 'HUMSS', 'test-school-koronadal-001', '09123456789', 'Male', true),
('test-student-002', 'MARIA ASUNCION', 'maria.asuncion@student.koronadal.edu.ph', 11, 'HUMSS 2', 'HUMSS', 'test-school-koronadal-001', '09123456790', 'Female', true),
('test-student-003', 'PEDRO SANTOS', 'pedro.santos@student.koronadal.edu.ph', 10, 'Section A', NULL, 'test-school-koronadal-001', '09123456791', 'Male', true);

-- Add attendance records with some absences
-- Student 1: 3 absences (should be flagged)
INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
VALUES 
('att-001', 'test-student-001', '2024-01-15', '08:00:00', '17:00:00', 'Present', 'On time'),
('att-002', 'test-student-001', '2024-01-16', NULL, NULL, 'Absent', 'No show'),
('att-003', 'test-student-001', '2024-01-17', '08:15:00', '17:00:00', 'Late', 'Traffic'),
('att-004', 'test-student-001', '2024-01-18', NULL, NULL, 'Absent', 'Sick'),
('att-005', 'test-student-001', '2024-01-19', '08:00:00', '17:00:00', 'Present', 'On time'),
('att-006', 'test-student-001', '2024-01-22', NULL, NULL, 'Absent', 'Family emergency');

-- Student 2: 2 absences (should not be flagged)
INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
VALUES 
('att-007', 'test-student-002', '2024-01-15', '08:00:00', '17:00:00', 'Present', 'On time'),
('att-008', 'test-student-002', '2024-01-16', '08:00:00', '17:00:00', 'Present', 'On time'),
('att-009', 'test-student-002', '2024-01-17', NULL, NULL, 'Absent', 'Sick'),
('att-010', 'test-student-002', '2024-01-18', '08:00:00', '17:00:00', 'Present', 'On time'),
('att-011', 'test-student-002', '2024-01-19', '08:00:00', '17:00:00', 'Present', 'On time'),
('att-012', 'test-student-002', '2024-01-22', NULL, NULL, 'Absent', 'Family matter');

-- Student 3: 4 absences (should be flagged)
INSERT IGNORE INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks)
VALUES 
('att-013', 'test-student-003', '2024-01-15', NULL, NULL, 'Absent', 'No show'),
('att-014', 'test-student-003', '2024-01-16', '08:00:00', '17:00:00', 'Present', 'On time'),
('att-015', 'test-student-003', '2024-01-17', NULL, NULL, 'Absent', 'Sick'),
('att-016', 'test-student-003', '2024-01-18', NULL, NULL, 'Absent', 'Personal issue'),
('att-017', 'test-student-003', '2024-01-19', '08:00:00', '17:00:00', 'Present', 'On time'),
('att-018', 'test-student-003', '2024-01-22', NULL, NULL, 'Absent', 'Transportation problem');


-- Verify the data
SELECT 'Test data added successfully!' as Status;
SELECT 'Guidance Counselor Login:' as Info, 'guidance_maria' as Username, 'password123' as Password;
SELECT 'Expected Results:' as Info, '2 students should be flagged (3+ absences)' as Details;
