-- Add test data with 3+ absences for guidance dashboard
-- This will make students appear in the guidance dashboard

-- First, let's check what students exist and add more absences to existing ones
-- Add more absences to existing student (6f3e9a34-75ab-48c0-a71f-1c4df2c1bd57) to make it 3+
INSERT INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks, CreatedAt, UpdatedAt)
VALUES 
(UUID(), '6f3e9a34-75ab-48c0-a71f-1c4df2c1bd57', '2025-10-27', NULL, NULL, 'Absent', 'Sick - 3rd absence', NOW(), NOW()),
(UUID(), '6f3e9a34-75ab-48c0-a71f-1c4df2c1bd57', '2025-10-28', NULL, NULL, 'Absent', 'Family emergency - 4th absence', NOW(), NOW());

-- Add another student with 3+ absences
INSERT INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks, CreatedAt, UpdatedAt)
VALUES 
(UUID(), 'test-student-003', '2025-10-25', NULL, NULL, 'Absent', 'Sick - 1st absence', NOW(), NOW()),
(UUID(), 'test-student-003', '2025-10-26', NULL, NULL, 'Absent', 'Personal issue - 2nd absence', NOW(), NOW()),
(UUID(), 'test-student-003', '2025-10-27', NULL, NULL, 'Absent', 'Transportation problem - 3rd absence', NOW(), NOW()),
(UUID(), 'test-student-003', '2025-10-28', NULL, NULL, 'Absent', 'Family matter - 4th absence', NOW(), NOW());

-- Add one more student with exactly 3 absences
INSERT INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks, CreatedAt, UpdatedAt)
VALUES 
(UUID(), 'test-student-001', '2025-10-25', NULL, NULL, 'Absent', 'Sick - 1st absence', NOW(), NOW()),
(UUID(), 'test-student-001', '2025-10-26', NULL, NULL, 'Absent', 'Personal issue - 2nd absence', NOW(), NOW()),
(UUID(), 'test-student-001', '2025-10-27', NULL, NULL, 'Absent', 'Family emergency - 3rd absence', NOW(), NOW());

-- Verify the data
SELECT 'Test data with 3+ absences added!' as Status;
SELECT 'Students with 3+ absences should now appear in guidance dashboard' as Message;
