-- Remove ScheduleStart/ScheduleEnd from subject table.
-- Schedule is set per class: in class_offering (advisor) and in teachersubject (legacy).

-- 1) Add schedule to teachersubject (for legacy teacher-subject flow)
ALTER TABLE teachersubject ADD COLUMN ScheduleStart TIME NULL;
ALTER TABLE teachersubject ADD COLUMN ScheduleEnd TIME NULL;

-- 2) Remove schedule from subject (subject = catalog only: name, grade, strand)
ALTER TABLE subject DROP COLUMN ScheduleStart;
ALTER TABLE subject DROP COLUMN ScheduleEnd;
