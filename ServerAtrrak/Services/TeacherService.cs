using AttrackSharedClass.Models;
using ServerAtrrak.Data;
using MySql.Data.MySqlClient;
using System.Data;

namespace ServerAtrrak.Services
{
    public class TeacherService
    {
        private readonly Dbconnection _dbConnection;

        public TeacherService(Dbconnection dbConnection)
        {
            _dbConnection = dbConnection;
        }

        public async Task<TeacherDashboardData?> GetTeacherDashboardDataAsync(string teacherId)
        {
            try
            {
                Console.WriteLine($"DEBUG: Looking for teacher with ID: {teacherId}");
                Console.WriteLine($"DEBUG: Query: SELECT u.UserId, u.Username, u.Email, u.UserType, u.TeacherId, t.FullName FROM user u LEFT JOIN teacher t ON u.TeacherId = t.TeacherId WHERE u.UserId = @teacherId AND u.UserType = 'Teacher'");
                using var connection = await _dbConnection.GetConnectionAsync();
                
                // Get teacher information
                var teacherQuery = @"
                    SELECT u.UserId, u.Username, u.Email, u.UserType, u.TeacherId, t.FullName 
                    FROM user u
                    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
                    WHERE u.UserId = @teacherId AND u.UserType = 'Teacher'";
                
                UserInfo? teacherInfo = null;
                string? actualTeacherId = null;
            using (var command = new MySqlCommand(teacherQuery, connection))
            {
                command.Parameters.AddWithValue("@teacherId", teacherId);
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        var userTypeString = reader.IsDBNull("UserType") ? "Teacher" : reader.GetString("UserType");
                        var userType = userTypeString switch
                        {
                            "Admin" => UserType.Admin,
                            "Teacher" => UserType.Teacher,
                            "Student" => UserType.Student,
                            "GuidanceCounselor" => UserType.GuidanceCounselor,
                            _ => UserType.Admin // Default fallback
                        };
                        
                        actualTeacherId = reader.IsDBNull("TeacherId") ? null : reader.GetString("TeacherId");
                        Console.WriteLine($"DEBUG: Found TeacherId: {actualTeacherId}");
                        Console.WriteLine($"DEBUG: UserId: {teacherId}, TeacherId: {actualTeacherId}");
                        
                        teacherInfo = new UserInfo
                        {
                            UserId = reader.IsDBNull("UserId") ? "" : reader.GetString("UserId"),
                            Username = reader.IsDBNull("Username") ? "" : reader.GetString("Username"),
                            Email = reader.IsDBNull("Email") ? "" : reader.GetString("Email"),
                            UserType = userType,
                            TeacherId = actualTeacherId
                        };
                    }
                }

                if (teacherInfo == null || string.IsNullOrEmpty(actualTeacherId))
                {
                    Console.WriteLine($"DEBUG: Teacher not found or no TeacherId. TeacherInfo: {teacherInfo != null}, TeacherId: {actualTeacherId}");
                    return null;
                }

                // Get teacher's class information with school name and strand
                var classQuery = @"
                    SELECT t.SchoolId, t.Gradelvl as GradeLevel, t.Section, t.Strand, s.SchoolName 
                    FROM teacher t
                    LEFT JOIN school s ON t.SchoolId = s.SchoolId
                    WHERE t.TeacherId = @actualTeacherId 
                    LIMIT 1";
                
                string? schoolId = null;
                int gradeLevel = 0;
                string? section = null;
                string? strand = null;
                string? schoolName = null;
                
                using (var command = new MySqlCommand(classQuery, connection))
                {
                    command.Parameters.AddWithValue("@actualTeacherId", actualTeacherId);
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        schoolId = reader.IsDBNull("SchoolId") ? null : reader.GetString("SchoolId");
                        gradeLevel = reader.IsDBNull("GradeLevel") ? 0 : reader.GetInt32("GradeLevel");
                        section = reader.IsDBNull("Section") ? null : reader.GetString("Section");
                        strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand");
                        schoolName = reader.IsDBNull("SchoolName") ? null : reader.GetString("SchoolName");
                    }
                }

                if (string.IsNullOrEmpty(schoolId))
                {
                    Console.WriteLine("DEBUG: No SchoolId found for teacher");
                    return null;
                }

                        Console.WriteLine($"DEBUG: Looking for students with SchoolId: {schoolId}, GradeLevel: {gradeLevel}, Section: {section}, Strand: {strand}");
                        
                        // Also check if there are any students in the school regardless of grade level
                        var debugQuery = "SELECT COUNT(*) as StudentCount FROM student WHERE SchoolId = @schoolId AND IsActive = 1";
                        using (var debugCommand = new MySqlCommand(debugQuery, connection))
                        {
                            debugCommand.Parameters.AddWithValue("@schoolId", schoolId);
                            var debugResult = await debugCommand.ExecuteScalarAsync();
                            Console.WriteLine($"DEBUG: Total students in school: {debugResult}");
                        }
                        
                        // Check students with same grade level
                        var gradeQuery = "SELECT COUNT(*) as StudentCount FROM student WHERE SchoolId = @schoolId AND GradeLevel = @gradeLevel AND IsActive = 1";
                        using (var gradeCommand = new MySqlCommand(gradeQuery, connection))
                        {
                            gradeCommand.Parameters.AddWithValue("@schoolId", schoolId);
                            gradeCommand.Parameters.AddWithValue("@gradeLevel", gradeLevel);
                            var gradeResult = await gradeCommand.ExecuteScalarAsync();
                            Console.WriteLine($"DEBUG: Students in same school and grade: {gradeResult}");
                        }
                        
                        // Check students with same section
                        var sectionQuery = "SELECT COUNT(*) as StudentCount FROM student WHERE SchoolId = @schoolId AND GradeLevel = @gradeLevel AND Section = @section AND IsActive = 1";
                        using (var sectionCommand = new MySqlCommand(sectionQuery, connection))
                        {
                            sectionCommand.Parameters.AddWithValue("@schoolId", schoolId);
                            sectionCommand.Parameters.AddWithValue("@gradeLevel", gradeLevel);
                            sectionCommand.Parameters.AddWithValue("@section", section ?? "");
                            var sectionResult = await sectionCommand.ExecuteScalarAsync();
                            Console.WriteLine($"DEBUG: Students in same school, grade, and section '{section ?? "NULL"}': {sectionResult}");
                        }
                        
                        // Check what sections students actually have in this school/grade
                        var actualSectionsQuery = "SELECT DISTINCT Section, COUNT(*) as Count FROM student WHERE SchoolId = @schoolId AND GradeLevel = @gradeLevel AND IsActive = 1 GROUP BY Section";
                        using (var actualSectionsCommand = new MySqlCommand(actualSectionsQuery, connection))
                        {
                            actualSectionsCommand.Parameters.AddWithValue("@schoolId", schoolId);
                            actualSectionsCommand.Parameters.AddWithValue("@gradeLevel", gradeLevel);
                            using var actualSectionsReader = await actualSectionsCommand.ExecuteReaderAsync();
                            Console.WriteLine($"DEBUG: Actual sections in database for this school/grade:");
                            while (await actualSectionsReader.ReadAsync())
                            {
                                var actualSection = actualSectionsReader.IsDBNull("Section") ? "NULL" : actualSectionsReader.GetString("Section");
                                var count = actualSectionsReader.GetInt32("Count");
                                Console.WriteLine($"DEBUG:   - Section '{actualSection}': {count} students");
                            }
                        }
                        
                        // Check what strands students actually have in this school/grade
                        var actualStrandsQuery = "SELECT DISTINCT Strand, COUNT(*) as Count FROM student WHERE SchoolId = @schoolId AND GradeLevel = @gradeLevel AND IsActive = 1 GROUP BY Strand";
                        using (var actualStrandsCommand = new MySqlCommand(actualStrandsQuery, connection))
                        {
                            actualStrandsCommand.Parameters.AddWithValue("@schoolId", schoolId);
                            actualStrandsCommand.Parameters.AddWithValue("@gradeLevel", gradeLevel);
                            using var actualStrandsReader = await actualStrandsCommand.ExecuteReaderAsync();
                            Console.WriteLine($"DEBUG: Actual strands in database for this school/grade:");
                            while (await actualStrandsReader.ReadAsync())
                            {
                                var actualStrand = actualStrandsReader.IsDBNull("Strand") ? "NULL" : actualStrandsReader.GetString("Strand");
                                var count = actualStrandsReader.GetInt32("Count");
                                Console.WriteLine($"DEBUG:   - Strand '{actualStrand}': {count} students");
                            }
                        }
                        
                        // Check section + strand combinations for grade 11-12
                        if (gradeLevel >= 11)
                        {
                            var comboQuery = "SELECT DISTINCT Section, Strand, COUNT(*) as Count FROM student WHERE SchoolId = @schoolId AND GradeLevel = @gradeLevel AND IsActive = 1 GROUP BY Section, Strand";
                            using (var comboCommand = new MySqlCommand(comboQuery, connection))
                            {
                                comboCommand.Parameters.AddWithValue("@schoolId", schoolId);
                                comboCommand.Parameters.AddWithValue("@gradeLevel", gradeLevel);
                                using var comboReader = await comboCommand.ExecuteReaderAsync();
                                Console.WriteLine($"DEBUG: Section + Strand combinations in database:");
                                while (await comboReader.ReadAsync())
                                {
                                    var actualSection = comboReader.IsDBNull("Section") ? "NULL" : comboReader.GetString("Section");
                                    var actualStrand = comboReader.IsDBNull("Strand") ? "NULL" : comboReader.GetString("Strand");
                                    var count = comboReader.GetInt32("Count");
                                    Console.WriteLine($"DEBUG:   - Section '{actualSection}' + Strand '{actualStrand}': {count} students");
                                }
                            }
                        }
                        
                        // Check ALL students in database
                        var allStudentsQuery = "SELECT COUNT(*) as StudentCount FROM student WHERE IsActive = 1";
                        using (var allCommand = new MySqlCommand(allStudentsQuery, connection))
                        {
                            var allResult = await allCommand.ExecuteScalarAsync();
                            Console.WriteLine($"DEBUG: Total active students in database: {allResult}");
                        }

                // Get students from the same school, grade level, section, and strand (if grade 11-12) with their absence counts
                var studentsQuery = @"
                    SELECT s.StudentId, s.FullName, s.Email, s.GradeLevel, s.Section, s.Strand, s.Gender, 
                           s.SchoolId, s.ParentsNumber, s.QRImage, s.CreatedAt, s.UpdatedAt, s.IsActive,
                           COALESCE(COUNT(CASE WHEN da.Status = 'Absent' THEN 1 END), 0) as AbsenceCount
                    FROM student s
                    LEFT JOIN daily_attendance da ON s.StudentId = da.StudentId
                    WHERE s.SchoolId = @schoolId AND s.GradeLevel = @gradeLevel AND s.IsActive = 1";

                // For grade 11-12, filter by both section AND strand
                if (gradeLevel >= 11)
                {
                    if (!string.IsNullOrEmpty(section))
                    {
                        studentsQuery += " AND s.Section = @section";
                    }
                    if (!string.IsNullOrEmpty(strand))
                    {
                        studentsQuery += " AND s.Strand = @strand";
                    }
                }
                // For grade 7-10, filter by section only
                else if (!string.IsNullOrEmpty(section))
                {
                    studentsQuery += " AND s.Section = @section";
                }

                studentsQuery += @"
                    GROUP BY s.StudentId, s.FullName, s.Email, s.GradeLevel, s.Section, s.Strand, s.Gender, 
                             s.SchoolId, s.ParentsNumber, s.QRImage, s.CreatedAt, s.UpdatedAt, s.IsActive";
                
                var students = new List<Student>();
                var studentsAtRisk = new List<Student>();
                
                using (var command = new MySqlCommand(studentsQuery, connection))
                {
                    command.Parameters.AddWithValue("@schoolId", schoolId);
                    command.Parameters.AddWithValue("@gradeLevel", gradeLevel);
                    
                    if (gradeLevel >= 11)
                    {
                        if (!string.IsNullOrEmpty(section))
                        {
                            command.Parameters.AddWithValue("@section", section);
                            Console.WriteLine($"DEBUG: Filtering Grade 11-12 by section: {section}");
                        }
                        if (!string.IsNullOrEmpty(strand))
                        {
                            command.Parameters.AddWithValue("@strand", strand);
                            Console.WriteLine($"DEBUG: Filtering Grade 11-12 by strand: {strand}");
                        }
                    }
                    else if (!string.IsNullOrEmpty(section))
                    {
                        command.Parameters.AddWithValue("@section", section);
                        Console.WriteLine($"DEBUG: Filtering Grade 7-10 by section: {section}");
                    }
                    
                    using var reader = await command.ExecuteReaderAsync();
                    Console.WriteLine($"DEBUG: Found {reader.FieldCount} columns in result");
                    int studentCount = 0;
                    while (await reader.ReadAsync())
                    {
                        studentCount++;
                        Console.WriteLine($"DEBUG: Processing student {studentCount}");
                        var absenceCount = reader.IsDBNull("AbsenceCount") ? 0 : reader.GetInt32("AbsenceCount");
                        
                        var student = new Student
                        {
                            StudentId = reader.IsDBNull("StudentId") ? "" : reader.GetString("StudentId"),
                            FullName = reader.IsDBNull("FullName") ? "" : reader.GetString("FullName"),
                            Email = reader.IsDBNull("Email") ? "" : reader.GetString("Email"),
                            GradeLevel = reader.IsDBNull("GradeLevel") ? 0 : reader.GetInt32("GradeLevel"),
                            Section = reader.IsDBNull("Section") ? "" : reader.GetString("Section"),
                            Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand"),
                            Gender = reader.IsDBNull("Gender") ? "" : reader.GetString("Gender"),
                            SchoolId = reader.IsDBNull("SchoolId") ? "" : reader.GetString("SchoolId"),
                            ParentsNumber = reader.IsDBNull("ParentsNumber") ? "" : reader.GetString("ParentsNumber"),
                            QRImage = reader.IsDBNull("QRImage") ? null : Convert.ToBase64String((byte[])reader["QRImage"]),
                            CreatedAt = reader.IsDBNull("CreatedAt") ? DateTime.Now : reader.GetDateTime("CreatedAt"),
                            UpdatedAt = reader.IsDBNull("UpdatedAt") ? DateTime.Now : reader.GetDateTime("UpdatedAt"),
                            IsActive = reader.IsDBNull("IsActive") ? true : reader.GetBoolean("IsActive"),
                            AbsenceCount = absenceCount,
                            Status = absenceCount >= 3 ? "At Risk" : absenceCount >= 1 ? "Warning" : "Good"
                        };
                        
                        students.Add(student);
                        
                        // Check if student is at risk (3+ absences)
                        if (absenceCount >= 3)
                        {
                            studentsAtRisk.Add(student);
                        }
                    }
                }

                Console.WriteLine($"DEBUG: Total students found: {students.Count}");
                
                return new TeacherDashboardData
                {
                    TeacherInfo = teacherInfo,
                    Students = students,
                    StudentsAtRisk = studentsAtRisk,
                    TotalStudents = students.Count,
                    StudentsAtRiskCount = studentsAtRisk.Count,
                    LastUpdated = DateTime.Now,
                    ClassInfo = new TeacherClassInfo
                    {
                        SchoolId = schoolId,
                        GradeLevel = gradeLevel,
                        Section = section ?? "",
                        Strand = strand,
                        SchoolName = schoolName ?? "",
                        FullName = teacherInfo.Username // Use username as fallback
                    }
                };
            }
            catch (Exception tex)
            {
                Console.WriteLine($"Error in GetTeacherDashboardDataAsync: {tex.Message}");
                return null;
            }
        }

        public async Task<List<Student>?> GetTeacherStudentsAsync(string teacherId)
        {
            try
            {
                using var connection = await _dbConnection.GetConnectionAsync();
                
                // First get the actual TeacherId from user table
                var userQuery = @"
                        SELECT TeacherId 
                        FROM user 
                        WHERE UserId = @userId AND UserType = 'Teacher'";
                
                string? actualTeacherId = null;
                using (var userCommand = new MySqlCommand(userQuery, connection))
                {
                    userCommand.Parameters.AddWithValue("@userId", teacherId);
                    using var userReader = await userCommand.ExecuteReaderAsync();
                    if (await userReader.ReadAsync())
                    {
                        actualTeacherId = userReader.IsDBNull("TeacherId") ? null : userReader.GetString("TeacherId");
                    }
                }
                
                if (string.IsNullOrEmpty(actualTeacherId))
                    return new List<Student>();
                
                // Get teacher's class information
                var classQuery = @"
                    SELECT SchoolId, Gradelvl as GradeLevel, Section, Strand
                    FROM teacher 
                    WHERE TeacherId = @actualTeacherId 
                    LIMIT 1";
                
                string? schoolId = null;
                int gradeLevel = 0;
                string? section = null;
                string? strand = null;
                
                using (var command = new MySqlCommand(classQuery, connection))
                {
                    command.Parameters.AddWithValue("@actualTeacherId", actualTeacherId);
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        schoolId = reader.IsDBNull("SchoolId") ? null : reader.GetString("SchoolId");
                        gradeLevel = reader.IsDBNull("GradeLevel") ? 0 : reader.GetInt32("GradeLevel");
                        section = reader.IsDBNull("Section") ? null : reader.GetString("Section");
                        strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand");
                    }
                }

                if (string.IsNullOrEmpty(schoolId))
                    return new List<Student>();

                // Get students from the same school, grade level, section, and strand (if grade 11-12)
                var studentsQuery = @"
                    SELECT StudentId, FullName, Email, GradeLevel, Section, Strand, Gender, SchoolId, ParentsNumber, QRImage, CreatedAt, UpdatedAt, IsActive
                    FROM student 
                    WHERE SchoolId = @schoolId AND GradeLevel = @gradeLevel AND IsActive = 1";

                // For grade 11-12, filter by both section AND strand
                if (gradeLevel >= 11)
                {
                    if (!string.IsNullOrEmpty(section))
                    {
                        studentsQuery += " AND Section = @section";
                    }
                    if (!string.IsNullOrEmpty(strand))
                    {
                        studentsQuery += " AND Strand = @strand";
                    }
                }
                // For grade 7-10, filter by section only
                else if (!string.IsNullOrEmpty(section))
                {
                    studentsQuery += " AND Section = @section";
                }
                
                var students = new List<Student>();
                
                using (var command = new MySqlCommand(studentsQuery, connection))
                {
                    command.Parameters.AddWithValue("@schoolId", schoolId);
                    command.Parameters.AddWithValue("@gradeLevel", gradeLevel);
                    
                    if (gradeLevel >= 11)
                    {
                        if (!string.IsNullOrEmpty(section))
                        {
                            command.Parameters.AddWithValue("@section", section);
                        }
                        if (!string.IsNullOrEmpty(strand))
                        {
                            command.Parameters.AddWithValue("@strand", strand);
                        }
                    }
                    else if (!string.IsNullOrEmpty(section))
                    {
                        command.Parameters.AddWithValue("@section", section);
                    }
                    
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var student = new Student
                        {
                        StudentId = reader.IsDBNull("StudentId") ? "" : reader.GetString("StudentId"),
                        FullName = reader.IsDBNull("FullName") ? "" : reader.GetString("FullName"),
                        Email = reader.IsDBNull("Email") ? "" : reader.GetString("Email"),
                        GradeLevel = reader.IsDBNull("GradeLevel") ? 0 : reader.GetInt32("GradeLevel"),
                        Section = reader.IsDBNull("Section") ? "" : reader.GetString("Section"),
                        Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand"),
                        Gender = reader.IsDBNull("Gender") ? "" : reader.GetString("Gender"),
                        SchoolId = reader.IsDBNull("SchoolId") ? "" : reader.GetString("SchoolId"),
                        ParentsNumber = reader.IsDBNull("ParentsNumber") ? "" : reader.GetString("ParentsNumber"),
                            QRImage = reader.IsDBNull("QRImage") ? null : Convert.ToBase64String((byte[])reader["QRImage"]),
                        CreatedAt = reader.IsDBNull("CreatedAt") ? DateTime.Now : reader.GetDateTime("CreatedAt"),
                        UpdatedAt = reader.IsDBNull("UpdatedAt") ? DateTime.Now : reader.GetDateTime("UpdatedAt"),
                        IsActive = reader.IsDBNull("IsActive") ? true : reader.GetBoolean("IsActive")
                        };
                        
                        students.Add(student);
                    }
                }

                return students;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetTeacherStudentsAsync: {ex.Message}");
                return null;
            }
        }

        public async Task<TeacherClassInfo?> GetTeacherClassInfoAsync(string teacherId)
        {
            try
            {
                using var connection = await _dbConnection.GetConnectionAsync();
                
                // Get teacher information
                var teacherQuery = @"
                    SELECT u.UserId, u.TeacherId, t.FullName 
                    FROM user u
                    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
                    WHERE u.UserId = @teacherId AND u.UserType = 'Teacher'";
                
                string? fullName = null;
                string? actualTeacherId = null;
                using (var command = new MySqlCommand(teacherQuery, connection))
                {
                    command.Parameters.AddWithValue("@teacherId", teacherId);
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        actualTeacherId = reader.IsDBNull("TeacherId") ? null : reader.GetString("TeacherId");
                        fullName = reader.IsDBNull("FullName") ? "" : reader.GetString("FullName");
                    }
                }

                if (fullName == null || string.IsNullOrEmpty(actualTeacherId))
                    return null;

                // Get teacher's class information
                var classQuery = @"
                    SELECT t.SchoolId, t.Gradelvl as GradeLevel, t.Section, t.Strand, s.SchoolName
                    FROM teacher t
                    LEFT JOIN school s ON t.SchoolId = s.SchoolId
                    WHERE t.TeacherId = @actualTeacherId 
                    LIMIT 1";
                
                TeacherClassInfo? classInfo = null;
                using (var command = new MySqlCommand(classQuery, connection))
                {
                    command.Parameters.AddWithValue("@actualTeacherId", actualTeacherId);
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        classInfo = new TeacherClassInfo
                        {
                            TeacherId = teacherId,
                            FullName = fullName,
                            GradeLevel = reader.GetInt32("GradeLevel"),
                            Section = reader.GetString("Section"),
                            Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand"),
                            SchoolId = reader.GetString("SchoolId"),
                            SchoolName = reader.IsDBNull("SchoolName") ? "" : reader.GetString("SchoolName"),
                            CreatedAt = DateTime.Now
                        };
                    }
                }

                return classInfo;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetTeacherClassInfoAsync: {ex.Message}");
                return null;
            }
        }

        public async Task<int> GetStudentAbsenceCountAsync(string studentId)
        {
            try
            {
                using var connection = await _dbConnection.GetConnectionAsync();
                
                var query = @"
                    SELECT COUNT(*) as AbsenceCount
                    FROM daily_attendance 
                    WHERE StudentId = @studentId AND Status = 'Absent'";
                
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@studentId", studentId);
                
                var result = await command.ExecuteScalarAsync();
                return result != null ? Convert.ToInt32(result) : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetStudentAbsenceCountAsync: {ex.Message}");
                return 0;
            }
        }

        public async Task<TeacherAttendanceSummary?> GetAttendanceSummaryAsync(string teacherId)
        {
            try
            {
                using var connection = await _dbConnection.GetConnectionAsync();
                
                // First get the actual TeacherId from user table
                var userQuery = @"
                    SELECT TeacherId 
                    FROM user 
                    WHERE UserId = @userId AND UserType = 'Teacher'";
                
                string? actualTeacherId = null;
                using (var userCommand = new MySqlCommand(userQuery, connection))
                {
                    userCommand.Parameters.AddWithValue("@userId", teacherId);
                    using var userReader = await userCommand.ExecuteReaderAsync();
                    if (await userReader.ReadAsync())
                    {
                        actualTeacherId = userReader.IsDBNull("TeacherId") ? null : userReader.GetString("TeacherId");
                    }
                }
                
                if (string.IsNullOrEmpty(actualTeacherId))
                    return null;
                
                // Get teacher's class information
                var classQuery = @"
                    SELECT SchoolId, Gradelvl as GradeLevel, Section 
                    FROM teacher 
                    WHERE TeacherId = @actualTeacherId 
                    LIMIT 1";
                
                string? schoolId = null;
                int gradeLevel = 0;
                string? section = null;
                
                using (var command = new MySqlCommand(classQuery, connection))
                {
                    command.Parameters.AddWithValue("@actualTeacherId", actualTeacherId);
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        schoolId = reader.IsDBNull("SchoolId") ? null : reader.GetString("SchoolId");
                        gradeLevel = reader.IsDBNull("GradeLevel") ? 0 : reader.GetInt32("GradeLevel");
                        section = reader.IsDBNull("Section") ? null : reader.GetString("Section");
                    }
                }

                if (string.IsNullOrEmpty(schoolId))
                    return null;

                // Get students count
                var studentsQuery = @"
                    SELECT COUNT(*) as StudentCount
                    FROM student 
                    WHERE SchoolId = @schoolId AND GradeLevel = @gradeLevel AND IsActive = 1";
                
                int totalStudents = 0;
                using (var command = new MySqlCommand(studentsQuery, connection))
                {
                    command.Parameters.AddWithValue("@schoolId", schoolId);
                    command.Parameters.AddWithValue("@gradeLevel", gradeLevel);
                    
                    var result = await command.ExecuteScalarAsync();
                    totalStudents = result != null ? Convert.ToInt32(result) : 0;
                }

                // Get today's attendance
                var today = DateTime.Today;
                var attendanceQuery = @"
                    SELECT 
                        COUNT(CASE WHEN da.Status = 'Present' THEN 1 END) as PresentToday,
                        COUNT(CASE WHEN da.Status = 'Absent' THEN 1 END) as AbsentToday
                    FROM student s
                    LEFT JOIN DailyAttendances da ON s.StudentId = da.StudentId AND da.Date = @today
                    WHERE s.SchoolId = @schoolId AND s.GradeLevel = @gradeLevel AND s.IsActive = 1";
                
                int presentToday = 0;
                int absentToday = 0;
                
                using (var command = new MySqlCommand(attendanceQuery, connection))
                {
                    command.Parameters.AddWithValue("@schoolId", schoolId);
                    command.Parameters.AddWithValue("@gradeLevel", gradeLevel);
                    command.Parameters.AddWithValue("@today", today);
                    
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        presentToday = reader.IsDBNull("PresentToday") ? 0 : reader.GetInt32("PresentToday");
                        absentToday = reader.IsDBNull("AbsentToday") ? 0 : reader.GetInt32("AbsentToday");
                    }
                }

                // Get students at risk count
                var atRiskQuery = @"
                    SELECT COUNT(*) as AtRiskCount
                    FROM student s
                    WHERE s.SchoolId = @schoolId AND s.GradeLevel = @gradeLevel AND s.IsActive = 1
                    AND (
                        SELECT COUNT(*) 
                        FROM daily_attendance da 
                        WHERE da.StudentId = s.StudentId AND da.Status = 'Absent'
                    ) >= 3";
                
                int studentsAtRisk = 0;
                using (var command = new MySqlCommand(atRiskQuery, connection))
                {
                    command.Parameters.AddWithValue("@schoolId", schoolId);
                    command.Parameters.AddWithValue("@gradeLevel", gradeLevel);
                    
                    var result = await command.ExecuteScalarAsync();
                    studentsAtRisk = result != null ? Convert.ToInt32(result) : 0;
                }

                var attendanceRate = totalStudents > 0 ? (double)presentToday / totalStudents * 100 : 0;

                return new TeacherAttendanceSummary
                {
                    TotalStudents = totalStudents,
                    PresentToday = presentToday,
                    AbsentToday = absentToday,
                    StudentsAtRisk = studentsAtRisk,
                    AttendanceRate = Math.Round(attendanceRate, 2),
                    Date = today
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAttendanceSummaryAsync: {ex.Message}");
                return null;
            }
        }
    }
}
