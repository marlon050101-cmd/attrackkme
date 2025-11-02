-- Add 1 more absence to existing students to make them have 3+ absences
-- This will make them appear in the guidance dashboard

-- Add 1 more absence to student 6f3e9a34-75ab-48c0-a71f-1c4df2c1bd57 (currently has 2 absences)
INSERT INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks, CreatedAt, UpdatedAt)
VALUES 
(UUID(), '6f3e9a34-75ab-48c0-a71f-1c4df2c1bd57', '2025-10-25', NULL, NULL, 'Absent', 'Sick - 3rd absence', NOW(), NOW());

-- Add 1 more absence to test-student-003 (currently has 2 absences in January)
INSERT INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks, CreatedAt, UpdatedAt)
VALUES 
(UUID(), 'test-student-003', '2025-10-25', NULL, NULL, 'Absent', 'Sick - 3rd absence', NOW(), NOW());

-- Verify the data
SELECT 'Added 1 more absence to make students have 3+ absences!' as Status;
SELECT 'Students should now appear in guidance dashboard' as Message;
