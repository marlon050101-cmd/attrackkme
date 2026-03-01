-- Per-subject attendance and advisor-student migration
-- Run this after backup. Adds AdvisorId to student, AdvisorId to teachersubject, and subject_attendance table.
-- If column already exists, skip that ALTER (MySQL has no IF NOT EXISTS for columns).

-- 1) Student: link to advisor (GuidanceCounselor TeacherId)
ALTER TABLE student ADD COLUMN AdvisorId VARCHAR(36) NULL;
ALTER TABLE student ADD INDEX idx_student_advisor (AdvisorId);

-- 2) TeacherSubject: optional advisor for this class
ALTER TABLE teachersubject ADD COLUMN AdvisorId VARCHAR(36) NULL;

-- 3) Per-subject attendance table
CREATE TABLE IF NOT EXISTS subject_attendance (
    SubjectAttendanceId VARCHAR(36) PRIMARY KEY,
    TeacherSubjectId VARCHAR(36) NOT NULL,
    StudentId VARCHAR(36) NOT NULL,
    Date DATE NOT NULL,
    Status VARCHAR(20) NOT NULL DEFAULT 'Present',
    Remarks TEXT NULL,
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY unique_subject_student_date (TeacherSubjectId, StudentId, Date),
    INDEX idx_ts_date (TeacherSubjectId, Date),
    INDEX idx_student_date (StudentId, Date),
    FOREIGN KEY (StudentId) REFERENCES student(StudentId) ON DELETE CASCADE
);

-- 4) Class offerings: advisor creates (section + subject + schedule); subject teacher picks (TeacherId set when assigned)
CREATE TABLE IF NOT EXISTS class_offering (
    ClassOfferingId VARCHAR(36) PRIMARY KEY,
    AdvisorId VARCHAR(36) NOT NULL,
    SubjectId VARCHAR(36) NOT NULL,
    GradeLevel INT NOT NULL,
    Section VARCHAR(50) NOT NULL,
    Strand VARCHAR(50) NULL,
    ScheduleStart TIME NOT NULL,
    ScheduleEnd TIME NOT NULL,
    TeacherId VARCHAR(36) NULL,
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_advisor (AdvisorId),
    INDEX idx_teacher (TeacherId),
    INDEX idx_advisor_grade_section (AdvisorId, GradeLevel, Section)
);

-- 5) Subject attendance: support class offering (use ClassOfferingId when from advisor flow)
ALTER TABLE subject_attendance ADD COLUMN ClassOfferingId VARCHAR(36) NULL;
ALTER TABLE subject_attendance ADD INDEX idx_class_offering_date (ClassOfferingId, Date);
ALTER TABLE subject_attendance ADD UNIQUE KEY unique_offering_student_date (ClassOfferingId, StudentId, Date);
-- Allow TeacherSubjectId to be null when using ClassOfferingId
ALTER TABLE subject_attendance MODIFY TeacherSubjectId VARCHAR(36) NULL;
