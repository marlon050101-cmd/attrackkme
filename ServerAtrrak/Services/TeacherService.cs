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
                Console.WriteLine($"DEBUG: Query: SELECT u.UserId, u.Username, u.Email, u.UserType, u.TeacherId, t.FullName FROM user u LEFT JOIN teacher t ON u.TeacherId = t.TeacherId WHERE u.UserId = @teacherId AND u.UserType = 'SubjectTeacher'");
                using var connection = await _dbConnection.GetConnectionAsync();
                
                // Get teacher information
                var teacherQuery = @"
                    SELECT u.UserId, u.Username, u.Email, u.UserType, u.TeacherId, t.FullName 
                    FROM user u
                    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
                    WHERE u.UserId = @teacherId AND u.UserType = 'SubjectTeacher'";
                
                UserInfo? teacherInfo = null;
                string? actualTeacherId = null;
                string? teacherFullName = null;
            using (var command = new MySqlCommand(teacherQuery, connection))
            {
                command.Parameters.AddWithValue("@teacherId", teacherId);
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        var userTypeString = reader.IsDBNull("UserType") ? "SubjectTeacher" : reader.GetString("UserType");
                        var userType = userTypeString switch
                        {
                            "Admin" => UserType.Admin,
                            "SubjectTeacher" => UserType.SubjectTeacher,
                            "Teacher" => UserType.SubjectTeacher, // legacy fallback
                            "Student" => UserType.Student,
                            "GuidanceCounselor" => UserType.GuidanceCounselor,
                            "Advisor" => UserType.Advisor,
                            _ => UserType.Admin
                        };
                        
                        actualTeacherId = reader.IsDBNull("TeacherId") ? null : reader.GetString("TeacherId");
                        teacherFullName = reader.IsDBNull("FullName") ? null : reader.GetString("FullName");
                        Console.WriteLine($"DEBUG: Found TeacherId: {actualTeacherId}, FullName: {teacherFullName}");
                        Console.WriteLine($"DEBUG: UserId: {teacherId}, TeacherId: {actualTeacherId}");
                        Console.WriteLine($"DEBUG: FullName from database: '{teacherFullName}' (IsNull: {reader.IsDBNull("FullName")})");
                        
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
                
                // If FullName is null or empty, query it directly from teacher table using actualTeacherId
                if (string.IsNullOrEmpty(teacherFullName) && !string.IsNullOrEmpty(actualTeacherId))
                {
                    var fullNameQuery = @"
                        SELECT FullName 
                        FROM teacher 
                        WHERE TeacherId = @actualTeacherId 
                        LIMIT 1";
                    
                    using (var fullNameCommand = new MySqlCommand(fullNameQuery, connection))
                    {
                        fullNameCommand.Parameters.AddWithValue("@actualTeacherId", actualTeacherId);
                        using var fullNameReader = await fullNameCommand.ExecuteReaderAsync();
                        if (await fullNameReader.ReadAsync())
                        {
                            teacherFullName = fullNameReader.IsDBNull("FullName") ? null : fullNameReader.GetString("FullName");
                            Console.WriteLine($"DEBUG: Re-queried FullName from teacher table: '{teacherFullName}'");
                        }
                    }
                }
                
                // Use FullName from teacher table, only fallback to username if still null/empty
                var finalFullName = !string.IsNullOrEmpty(teacherFullName) ? teacherFullName : teacherInfo.Username;
                Console.WriteLine($"DEBUG: Setting ClassInfo.FullName to: '{finalFullName}' (from teacherFullName: '{teacherFullName}', fallback to username: '{teacherInfo.Username}')");
                
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
                        FullName = finalFullName // Use actual FullName from teacher table, fallback to username if not available
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
                        WHERE UserId = @userId AND UserType = 'SubjectTeacher'";
                
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
                    WHERE u.UserId = @teacherId AND u.UserType = 'SubjectTeacher'";
                
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
                        Console.WriteLine($"DEBUG: GetTeacherClassInfoAsync - Found FullName: '{fullName}', TeacherId: '{actualTeacherId}'");
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

        public async Task<bool> UpdateTeacherFullNameAsync(string teacherId, string fullName)
        {
            try
            {
                using var connection = await _dbConnection.GetConnectionAsync();
                
                // First get the actual TeacherId from user table
                var userQuery = @"
                    SELECT TeacherId 
                    FROM user 
                    WHERE UserId = @userId AND UserType = 'SubjectTeacher'";
                
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
                {
                    Console.WriteLine($"DEBUG: Teacher not found for UserId: {teacherId}");
                    return false;
                }
                
                // Update the FullName in teacher table
                var updateQuery = @"
                    UPDATE teacher 
                    SET FullName = @fullName, UpdatedAt = @updatedAt
                    WHERE TeacherId = @teacherId";
                
                using (var command = new MySqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@fullName", fullName);
                    command.Parameters.AddWithValue("@updatedAt", DateTime.Now);
                    command.Parameters.AddWithValue("@teacherId", actualTeacherId);
                    
                    var rowsAffected = await command.ExecuteNonQueryAsync();
                    Console.WriteLine($"DEBUG: Updated teacher FullName. Rows affected: {rowsAffected}, TeacherId: {actualTeacherId}, New FullName: {fullName}");
                    
                    return rowsAffected > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateTeacherFullNameAsync: {ex.Message}");
                return false;
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
                    WHERE UserId = @userId AND UserType = 'SubjectTeacher'";
                
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
        public async Task<List<PendingTeacherInfo>> GetPendingTeachersAsync(string schoolId)
        {
            try
            {
                using var connection = await _dbConnection.GetConnectionAsync();
                
                // DEBUG: Check ALL pending users regardless of school
                var debugQuery = @"
                    SELECT u.Username, u.UserType, u.IsApproved, t.SchoolId as TeacherSchoolId
                    FROM user u
                    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
                    WHERE u.IsApproved = 0 AND (u.UserType = 'Teacher' OR u.UserType = 'SubjectTeacher')";
                
                using (var cmd = new MySqlCommand(debugQuery, connection))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    Console.WriteLine("DEBUG: --- START DB TRACE ---");
                    Console.WriteLine($"DEBUG: Searching for SchoolId: [{schoolId}]");
                    while (await reader.ReadAsync())
                    {
                        Console.WriteLine($"DEBUG: Pending User: {reader.GetString("Username")} | Type: {reader.GetString("UserType")} | TeacherSchool: {reader.GetValue(3)}");
                    }
                    Console.WriteLine("DEBUG: --- END DB TRACE ---");
                }

                var query = @"
                    SELECT u.UserId, u.Username, u.Email, u.UserType, u.CreatedAt as RegisteredAt,
                           COALESCE(t.FullName, u.Username) as FullName, 
                           t.SchoolId, t.Gradelvl as GradeLevel, t.Section, t.Strand,
                           s.SchoolName
                    FROM user u
                    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
                    LEFT JOIN school s ON t.SchoolId = s.SchoolId
                    WHERE t.SchoolId = @schoolId
                    AND u.IsApproved = 0 
                    AND (u.UserType = 'Teacher' OR u.UserType = 'SubjectTeacher')";
                
                var pendingTeachers = new List<PendingTeacherInfo>();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@schoolId", schoolId);
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var info = new PendingTeacherInfo
                        {
                            UserId = reader.GetString("UserId"),
                            Username = reader.GetString("Username"),
                            Email = reader.GetString("Email"),
                            FullName = reader.IsDBNull("FullName") ? "" : reader.GetString("FullName"),
                            SchoolId = reader.IsDBNull("SchoolId") ? "" : reader.GetString("SchoolId"),
                            SchoolName = reader.IsDBNull("SchoolName") ? "" : reader.GetString("SchoolName"),
                            RegisteredAt = reader.GetDateTime("RegisteredAt"),
                            UserType = reader.GetString("UserType"),
                            GradeLevel = reader.IsDBNull("GradeLevel") ? (int?)null : reader.GetInt32("GradeLevel"),
                            Section = reader.IsDBNull("Section") ? null : reader.GetString("Section"),
                            Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand")
                        };
                        pendingTeachers.Add(info);
                    }
                }
                return pendingTeachers;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetPendingTeachersAsync: {ex.Message}");
                return new List<PendingTeacherInfo>();
            }
        }

        public async Task<bool> ApproveTeacherAsync(string userId, UserType targetRole)
        {
            try
            {
                using var connection = await _dbConnection.GetConnectionAsync();
                
                // Map enum to string for DB (SubjectTeacher -> 'SubjectTeacher')
                string roleString = targetRole == UserType.SubjectTeacher ? "SubjectTeacher" : targetRole.ToString();

                var query = "UPDATE user SET IsApproved = 1, IsActive = 1, UserType = @role WHERE UserId = @userId";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@role", roleString);
                command.Parameters.AddWithValue("@userId", userId);
                var rows = await command.ExecuteNonQueryAsync();
                return rows > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ApproveTeacherAsync: {ex.Message}");
                return false;
            }
        }
        public async Task<object> GetHeadStatsAsync(string schoolId)
        {
            try
            {
                using var connection = await _dbConnection.GetConnectionAsync();
                
                // Active Teachers
                var teachersQuery = @"
                    SELECT COUNT(*) 
                    FROM user u 
                    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId 
                    WHERE t.SchoolId = @schoolId 
                    AND u.IsActive = 1 
                    AND (u.UserType = 'Teacher' OR u.UserType = 'SubjectTeacher')";
                using var teacherCommand = new MySqlCommand(teachersQuery, connection);
                teacherCommand.Parameters.AddWithValue("@schoolId", schoolId);
                var activeTeachers = Convert.ToInt32(await teacherCommand.ExecuteScalarAsync());

                // Active Students
                var studentsQuery = @"
                    SELECT COUNT(*) 
                    FROM student 
                    WHERE SchoolId = @schoolId AND IsActive = 1";
                using var studentCommand = new MySqlCommand(studentsQuery, connection);
                studentCommand.Parameters.AddWithValue("@schoolId", schoolId);
                var activeStudents = Convert.ToInt32(await studentCommand.ExecuteScalarAsync());

                // Pending Approvals
                var pendingQuery = @"
                    SELECT COUNT(*) 
                    FROM user u 
                    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId 
                    WHERE t.SchoolId = @schoolId 
                    AND u.IsApproved = 0 
                    AND (u.UserType = 'Teacher' OR u.UserType = 'SubjectTeacher')";
                using var pendingCommand = new MySqlCommand(pendingQuery, connection);
                pendingCommand.Parameters.AddWithValue("@schoolId", schoolId);
                var pendingApprovals = Convert.ToInt32(await pendingCommand.ExecuteScalarAsync());

                return new
                {
                    ActiveTeachers = activeTeachers,
                    ActiveStudents = activeStudents,
                    PendingApprovals = pendingApprovals
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetHeadStatsAsync: {ex.Message}");
                return new { ActiveTeachers = 0, ActiveStudents = 0, PendingApprovals = 0 };
            }
        }
        public async Task<object> GetTeacherApprovalDiagnosticsAsync(string schoolId)
        {
            try
            {
                using var connection = await _dbConnection.GetConnectionAsync();
                
                var diagnostics = new
                {
                    CurrentSchoolId = schoolId,
                    ServerTime = DateTime.Now,
                    PendingUsers = new List<object>()
                };

                var query = @"
                    SELECT u.Username, u.UserType, u.IsApproved, 
                           t.SchoolId as TeacherTableSchoolId,
                           u.TeacherId,
                           u.CreatedAt
                    FROM user u
                    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
                    WHERE u.IsApproved = 0 
                    AND (u.UserType = 'Teacher' OR u.UserType = 'SubjectTeacher')";

                using (var cmd = new MySqlCommand(query, connection))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var teacherSchool = reader.IsDBNull("TeacherTableSchoolId") ? "" : reader.GetString("TeacherTableSchoolId");
                        diagnostics.PendingUsers.Add(new
                        {
                            Username = reader.GetString("Username"),
                            Type = reader.GetString("UserType"),
                            UserSchool = "N/A",
                            TeacherSchool = string.IsNullOrEmpty(teacherSchool) ? "NULL" : teacherSchool,
                            TeacherId = reader.IsDBNull("TeacherId") ? "NULL" : reader.GetString("TeacherId"),
                            RegisteredAt = reader.GetDateTime("CreatedAt"),
                            IsMatch = teacherSchool == schoolId
                        });
                    }
                }
                return diagnostics;
            }
            catch (Exception ex)
            {
                return new { Error = ex.Message };
            }
        }
        public async Task<List<PendingTeacherInfo>> GetAllTeachersBySchoolAsync(string schoolId)
        {
            try
            {
                using var connection = await _dbConnection.GetConnectionAsync();
                var query = @"
                    SELECT u.UserId, u.Username, u.Email, u.Password, u.UserType, u.CreatedAt as RegisteredAt,
                           u.IsApproved, u.IsActive,
                           COALESCE(t.FullName, u.Username) as FullName, 
                           t.SchoolId, t.Gradelvl as GradeLevel, t.Section, t.Strand,
                           s.SchoolName
                    FROM user u
                    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
                    LEFT JOIN school s ON t.SchoolId = s.SchoolId
                    WHERE t.SchoolId = @schoolId
                    AND (u.UserType = 'Teacher' OR u.UserType = 'SubjectTeacher' OR u.UserType = 'Advisor')";

                var teachers = new List<PendingTeacherInfo>();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@schoolId", schoolId);
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var info = new PendingTeacherInfo
                        {
                            UserId = reader.GetString("UserId"),
                            Username = reader.GetString("Username"),
                            Email = reader.GetString("Email"),
                            FullName = reader.IsDBNull("FullName") ? "" : reader.GetString("FullName"),
                            SchoolId = reader.IsDBNull("SchoolId") ? "" : reader.GetString("SchoolId"),
                            SchoolName = reader.IsDBNull("SchoolName") ? "" : reader.GetString("SchoolName"),
                            RegisteredAt = reader.GetDateTime("RegisteredAt"),
                            UserType = reader.GetString("UserType"),
                            Password = reader.IsDBNull("Password") ? "" : reader.GetString("Password"),
                            GradeLevel = reader.IsDBNull("GradeLevel") ? (int?)null : reader.GetInt32("GradeLevel"),
                            Section = reader.IsDBNull("Section") ? null : reader.GetString("Section"),
                            Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand")
                        };
                        teachers.Add(info);
                    }
                }
                return teachers;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAllTeachersBySchoolAsync: {ex.Message}");
                return new List<PendingTeacherInfo>();
            }
        }

        public async Task<bool> UpdateTeacherAsync(string userId, UpdateTeacherRequest request)
        {
            try
            {
                using var connection = await _dbConnection.GetConnectionAsync();
                Console.WriteLine($"DEBUG: UpdateTeacherAsync - Starting for UserId: {userId}");
                
                // 1. Get TeacherId from user table
                var userQuery = "SELECT TeacherId FROM user WHERE UserId = @userId";
                string? teacherId = null;
                using (var cmd = new MySqlCommand(userQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    var result = await cmd.ExecuteScalarAsync();
                    teacherId = result?.ToString();
                }

                Console.WriteLine($"DEBUG: UpdateTeacherAsync - Found TeacherId: {teacherId}");
                if (string.IsNullOrEmpty(teacherId)) 
                {
                    Console.WriteLine($"DEBUG: UpdateTeacherAsync - TeacherId not found for UserId: {userId}");
                    return false;
                }

                // 2. Update user table (Email)
                var updateUser = "UPDATE user SET Email = @email WHERE UserId = @userId";
                using (var cmd = new MySqlCommand(updateUser, connection))
                {
                    cmd.Parameters.AddWithValue("@email", request.Email);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    var userRows = await cmd.ExecuteNonQueryAsync();
                    Console.WriteLine($"DEBUG: UpdateTeacherAsync - User table updated, rows affected: {userRows}");
                }

                // 3. Update teacher table (FullName, GradeLevel, Section, Strand)
                var updateTeacher = @"
                    UPDATE teacher 
                    SET FullName = @fullName, 
                        Gradelvl = @gradeLevel, 
                        Section = @section, 
                        Strand = @strand,
                        UpdatedAt = @updatedAt
                    WHERE TeacherId = @teacherId";
                
                using (var cmd = new MySqlCommand(updateTeacher, connection))
                {
                    cmd.Parameters.AddWithValue("@fullName", request.FullName);
                    cmd.Parameters.AddWithValue("@gradeLevel", request.GradeLevel ?? 0);
                    cmd.Parameters.AddWithValue("@section", request.Section ?? "");
                    cmd.Parameters.AddWithValue("@strand", (object)request.Strand ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@updatedAt", DateTime.Now);
                    cmd.Parameters.AddWithValue("@teacherId", teacherId);
                    
                    var rows = await cmd.ExecuteNonQueryAsync();
                    Console.WriteLine($"DEBUG: UpdateTeacherAsync - Teacher table updated, rows affected: {rows}");
                    return rows > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG: Error in UpdateTeacherAsync: {ex.Message}");
                Console.WriteLine($"DEBUG: StackTrace: {ex.StackTrace}");
                return false;
            }
        }
    }
}
