-- Migration to support per-subject Time In and Time Out
-- Run this to update the subject_attendance table for the new scanner features.

-- 1. Add columns for scan timestamps
ALTER TABLE subject_attendance 
ADD COLUMN TimeIn DATETIME NULL AFTER Status,
ADD COLUMN TimeOut DATETIME NULL AFTER TimeIn;

-- 2. Ensure ClassOfferingId exists (for adviser subjects)
-- If you haven't run previous migrations, uncomment this:
-- ALTER TABLE subject_attendance ADD COLUMN ClassOfferingId VARCHAR(36) NULL AFTER StudentId;
-- ALTER TABLE subject_attendance ADD INDEX idx_class_offering (ClassOfferingId);
-- ALTER TABLE subject_attendance ADD UNIQUE KEY unique_offering_student_date (ClassOfferingId, StudentId, Date);
