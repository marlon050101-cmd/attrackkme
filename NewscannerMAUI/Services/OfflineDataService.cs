using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Data;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AttrackSharedClass.Models;

namespace NewscannerMAUI.Services
{
    public class OfflineDataService
    {
        private readonly string _databasePath;
        private readonly string _connectionString;

        public OfflineDataService()
        {
            // Use AppDataDirectory (guaranteed to work, no permissions needed)
            _databasePath = Path.Combine(FileSystem.AppDataDirectory, "attrak_offline.db");
            _connectionString = $"Data Source={_databasePath};Cache=Shared;Mode=ReadWriteCreate";
            InitializeDatabase();
            
            // Log the database path for debugging
            System.Diagnostics.Debug.WriteLine($"SQLite Database Path: {_databasePath}");
            System.Diagnostics.Debug.WriteLine($"App Data Directory: {FileSystem.AppDataDirectory}");
            System.Diagnostics.Debug.WriteLine($"Database file exists: {File.Exists(_databasePath)}");
        }

        private void InitializeDatabase()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== Initializing SQLite Database ===");
                System.Diagnostics.Debug.WriteLine($"Database path: {_databasePath}");
                System.Diagnostics.Debug.WriteLine($"App data directory: {FileSystem.AppDataDirectory}");
                System.Diagnostics.Debug.WriteLine($"Database exists before init: {File.Exists(_databasePath)}");
                
                // Ensure the directory exists and has proper permissions
                var directory = Path.GetDirectoryName(_databasePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    System.Diagnostics.Debug.WriteLine($"Created directory: {directory}");
                }
                
                // Ensure the database file has proper write permissions
                if (File.Exists(_databasePath))
                {
                    try
                    {
                        // Remove readonly attribute if it exists
                        var fileInfo = new FileInfo(_databasePath);
                        if (fileInfo.IsReadOnly)
                        {
                            fileInfo.IsReadOnly = false;
                            System.Diagnostics.Debug.WriteLine("Removed readonly attribute from database file");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not modify file attributes: {ex.Message}");
                    }
                }
                
                // Check if database exists and has data (only after tables are created)
                if (File.Exists(_databasePath))
                {
                    System.Diagnostics.Debug.WriteLine("Database file exists, will check for existing data after table creation...");
                    
                    // Check file permissions
                    var fileInfo = new FileInfo(_databasePath);
                    System.Diagnostics.Debug.WriteLine($"Database file size: {fileInfo.Length} bytes");
                    System.Diagnostics.Debug.WriteLine($"Database file attributes: {fileInfo.Attributes}");
                }
                
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                System.Diagnostics.Debug.WriteLine("Database connection opened successfully");

                // Create offline daily attendance table (matches server daily_attendance table)
                var createDailyAttendanceTable = @"
                    CREATE TABLE IF NOT EXISTS offline_daily_attendance (
                        attendance_id TEXT PRIMARY KEY,
                        student_id TEXT NOT NULL,
                        date TEXT NOT NULL,
                        time_in TEXT,
                        time_out TEXT,
                        status TEXT NOT NULL DEFAULT 'Present',
                        remarks TEXT,
                        device_id TEXT,
                        is_synced INTEGER DEFAULT 0,
                        attendance_type TEXT,
                        teacher_id TEXT,
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    )";

                // Create offline attendance table (matches server attendance table)
                var createAttendanceTable = @"
                    CREATE TABLE IF NOT EXISTS offline_attendance (
                        attendance_id TEXT PRIMARY KEY,
                        student_id TEXT NOT NULL,
                        subject_id TEXT,
                        teacher_id TEXT,
                        timestamp DATETIME NOT NULL,
                        status TEXT NOT NULL DEFAULT 'Present',
                        attendance_type TEXT NOT NULL,
                        device_id TEXT,
                        is_synced INTEGER DEFAULT 0,
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    )";

                // Create offline users table for authentication
                var createUsersTable = @"
                    CREATE TABLE IF NOT EXISTS offline_users (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        username TEXT UNIQUE NOT NULL,
                        password_hash TEXT NOT NULL,
                        user_type TEXT NOT NULL,
                        full_name TEXT,
                        is_active INTEGER DEFAULT 1,
                        last_login DATETIME,
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    )";

                // Create sync log table
                var createSyncLogTable = @"
                    CREATE TABLE IF NOT EXISTS sync_log (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        sync_type TEXT NOT NULL,
                        record_count INTEGER,
                        sync_time DATETIME DEFAULT CURRENT_TIMESTAMP,
                        status TEXT NOT NULL,
                        error_message TEXT
                    )";

                // Create student names cache table with full profile for validation
                var createStudentNamesCacheTable = @"
                    CREATE TABLE IF NOT EXISTS student_names_cache (
                        student_id TEXT PRIMARY KEY,
                        student_name TEXT NOT NULL,
                        grade_level INTEGER,
                        section TEXT,
                        strand TEXT,
                        school_id TEXT,
                        cached_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    )";

                System.Diagnostics.Debug.WriteLine("Creating offline_daily_attendance table...");
                var command1 = new SqliteCommand(createDailyAttendanceTable, connection);
                command1.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("offline_daily_attendance table created successfully");

                System.Diagnostics.Debug.WriteLine("Creating offline_attendance table...");
                var command2 = new SqliteCommand(createAttendanceTable, connection);
                command2.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("offline_attendance table created successfully");

                System.Diagnostics.Debug.WriteLine("Creating offline_users table...");
                var command3 = new SqliteCommand(createUsersTable, connection);
                command3.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("offline_users table created successfully");

                System.Diagnostics.Debug.WriteLine("Creating sync_log table...");
                var command4 = new SqliteCommand(createSyncLogTable, connection);
                command4.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("sync_log table created successfully");

                System.Diagnostics.Debug.WriteLine("Creating student_names_cache table...");
                var command5 = new SqliteCommand(createStudentNamesCacheTable, connection);
                command5.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("student_names_cache table created successfully");

                // === MIGRATIONS: Safely add any new columns to existing tables ===

                // Migration for offline_daily_attendance
                void TryAddColumn(string table, string column, string type)
                {
                    try { new SqliteCommand($"ALTER TABLE {table} ADD COLUMN {column} {type}", connection).ExecuteNonQuery(); }
                    catch { /* Column already exists */ }
                }

                TryAddColumn("offline_daily_attendance", "attendance_type", "TEXT");
                TryAddColumn("offline_daily_attendance", "teacher_id", "TEXT");
                TryAddColumn("offline_daily_attendance", "remarks", "TEXT");
                TryAddColumn("student_names_cache", "grade_level", "INTEGER");
                TryAddColumn("student_names_cache", "section", "TEXT");
                TryAddColumn("student_names_cache", "strand", "TEXT");
                TryAddColumn("student_names_cache", "school_id", "TEXT");

                System.Diagnostics.Debug.WriteLine("Schema migrations complete.");

                // Now check for existing data after tables are created
                try
                {
                    // Check existing
                    var checkCommand = new SqliteCommand("SELECT COUNT(*) FROM offline_daily_attendance", connection);
                    var count = checkCommand.ExecuteScalar();
                    System.Diagnostics.Debug.WriteLine($"Existing records in offline_daily_attendance: {count}");
                    
                    // One-time fix for existing "Auto" type records that are causing sync issues
                    var fixAutoCommand = new SqliteCommand(
                        "UPDATE offline_daily_attendance SET attendance_type = 'TimeIn' WHERE attendance_type = 'Auto' OR attendance_type IS NULL", 
                        connection);
                    var fixedCount = fixAutoCommand.ExecuteNonQuery();
                    if (fixedCount > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"Fixed {fixedCount} historical records with 'Auto' or NULL attendance_type");
                    }

                    // CRITICAL CLEANUP: Delete any previously synced records so they don't pile up in the database / pending list 
                    // (if they somehow show up due to date bugs) over multiple days
                    var cleanupCommand = new SqliteCommand("DELETE FROM offline_daily_attendance WHERE is_synced = 1", connection);
                    var cleanedCount = cleanupCommand.ExecuteNonQuery();
                    if (cleanedCount > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"Cleaned up {cleanedCount} old synced records from storage");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error checking existing data / cleaning up: {ex.Message}");
                }

                System.Diagnostics.Debug.WriteLine("Offline database initialized successfully");
                
                // Test that the database is writable
                try
                {
                    var testCommand = new SqliteCommand("SELECT 1", connection);
                    var result = testCommand.ExecuteScalar();
                    System.Diagnostics.Debug.WriteLine($"Database write test successful: {result}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Database write test failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing offline database: {ex.Message}");
            }
        }

        // Offline Authentication Methods
        public async Task<bool> AuthenticateUserOfflineAsync(string username, string password)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var command = new SqliteCommand(
                    "SELECT password_hash, user_type, full_name FROM offline_users WHERE username = @username AND is_active = 1",
                    connection);
                command.Parameters.AddWithValue("@username", username);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var storedHash = reader.GetString("password_hash");
                    var userType = reader.GetString("user_type");
                    var fullName = reader.GetString("full_name");

                    // Simple password verification (in production, use proper hashing)
                    if (storedHash == password) // This should be proper hash comparison
                    {
                        // Update last login
                        await UpdateLastLoginAsync(username);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in offline authentication: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> AddOfflineUserAsync(string username, string password, string userType, string fullName)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var command = new SqliteCommand(
                    "INSERT INTO offline_users (username, password_hash, user_type, full_name) VALUES (@username, @password, @userType, @fullName)",
                    connection);
                command.Parameters.AddWithValue("@username", username);
                command.Parameters.AddWithValue("@password", password); // Should be hashed
                command.Parameters.AddWithValue("@userType", userType);
                command.Parameters.AddWithValue("@fullName", fullName);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding offline user: {ex.Message}");
                return false;
            }
        }

        private async Task UpdateLastLoginAsync(string username)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var command = new SqliteCommand(
                    "UPDATE offline_users SET last_login = CURRENT_TIMESTAMP WHERE username = @username",
                    connection);
                command.Parameters.AddWithValue("@username", username);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating last login: {ex.Message}");
            }
        }

        // Offline Attendance Methods
        public async Task<bool> SaveOfflineAttendanceAsync(string studentId, string attendanceType, string? deviceId = null, DateTime? scanTime = null, bool isSynced = false, string? studentName = null, string? teacherId = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== SAVING OFFLINE ATTENDANCE ===");
                System.Diagnostics.Debug.WriteLine($"StudentId: {studentId}");
                System.Diagnostics.Debug.WriteLine($"AttendanceType: {attendanceType}");
                System.Diagnostics.Debug.WriteLine($"DeviceId: {deviceId}");
                System.Diagnostics.Debug.WriteLine($"Database path: {_databasePath}");
                System.Diagnostics.Debug.WriteLine($"Database exists: {File.Exists(_databasePath)}");

                // Ensure database is initialized
                InitializeDatabase();

                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                System.Diagnostics.Debug.WriteLine("Database connection opened successfully");

                // Use daily attendance table for TimeIn/TimeOut
                if (attendanceType == "TimeIn" || attendanceType == "TimeOut")
                {
                    var today = DateTime.Today.ToString("yyyy-MM-dd");
                    var timeValue = DateTime.Now.ToString("HH:mm");
                    var deviceIdValue = deviceId ?? GetDeviceId();
                    
                    // Check if this attendance type already exists for this student today
                    var checkQuery = "SELECT COUNT(*) FROM offline_daily_attendance WHERE student_id = @studentId AND date = @date AND attendance_type = @attendanceType";
                    using var checkCommand = new SqliteCommand(checkQuery, connection);
                    checkCommand.Parameters.AddWithValue("@studentId", studentId);
                    checkCommand.Parameters.AddWithValue("@date", today);
                    checkCommand.Parameters.AddWithValue("@attendanceType", attendanceType);
                    
                    var existingCount = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                    
                    if (existingCount > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"{attendanceType} already exists for student {studentId} on {today}. Skipping duplicate {attendanceType}.");
                        return true; // Don't create duplicate
                    }
                    
                    // Determine Status based on Time (Business Rules)
                    string status = "Present";
                    if (attendanceType == "TimeIn")
                    {
                        var nowTime = DateTime.Now.TimeOfDay;
                        // Morning: After 7:00 AM is late
                        if (nowTime > new TimeSpan(7, 0, 0) && nowTime < new TimeSpan(11, 0, 0))
                        {
                            status = "Late";
                        }
                        // Break: 11:00 AM - 1:04 PM is NOT late
                        else if (nowTime >= new TimeSpan(11, 0, 0) && nowTime <= new TimeSpan(13, 4, 59))
                        {
                            status = "Present";
                        }
                        // Afternoon: 1:05 PM or later is late
                        else if (nowTime >= new TimeSpan(13, 5, 0))
                        {
                            status = "Late";
                        }
                    }

                    // Create a new record for this TimeIn/TimeOut scan
                    // This ensures both TimeIn and TimeOut appear separately in pending list
                    var attendanceId = Guid.NewGuid().ToString();
                    
                    System.Diagnostics.Debug.WriteLine($"Creating new {attendanceType} record for student {studentId} on {today}. Status: {status}. Record ID: {attendanceId}");
                        
                    var insertCommand = new SqliteCommand(
                        "INSERT INTO offline_daily_attendance (attendance_id, student_id, date, time_in, time_out, status, device_id, is_synced, attendance_type, teacher_id) VALUES (@attendanceId, @studentId, @date, @timeIn, @timeOut, @status, @deviceId, @isSynced, @attendanceType, @teacherId)",
                        connection);
                    
                    insertCommand.Parameters.AddWithValue("@attendanceId", attendanceId);
                    insertCommand.Parameters.AddWithValue("@studentId", studentId);
                    insertCommand.Parameters.AddWithValue("@date", today);
                    insertCommand.Parameters.AddWithValue("@timeIn", attendanceType == "TimeIn" ? timeValue : (object)DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@timeOut", attendanceType == "TimeOut" ? timeValue : (object)DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@status", status);
                    insertCommand.Parameters.AddWithValue("@deviceId", deviceIdValue);
                    insertCommand.Parameters.AddWithValue("@isSynced", isSynced ? 1 : 0);
                    insertCommand.Parameters.AddWithValue("@attendanceType", attendanceType);
                    insertCommand.Parameters.AddWithValue("@teacherId", teacherId ?? "Unknown");
                        
                    var insertResult = await insertCommand.ExecuteNonQueryAsync();
                    System.Diagnostics.Debug.WriteLine($"Created new {attendanceType} record. Rows affected: {insertResult}");
                }
                else
                {
                    // Use regular attendance table for other types
                    var command = new SqliteCommand(
                        "INSERT INTO offline_attendance (attendance_id, student_id, timestamp, attendance_type, device_id, is_synced) VALUES (@attendanceId, @studentId, @timestamp, @attendanceType, @deviceId, @isSynced)",
                        connection);
                    command.Parameters.AddWithValue("@attendanceId", Guid.NewGuid().ToString());
                    command.Parameters.AddWithValue("@studentId", studentId);
                    command.Parameters.AddWithValue("@timestamp", DateTime.Now);
                    command.Parameters.AddWithValue("@attendanceType", attendanceType);
                    command.Parameters.AddWithValue("@deviceId", deviceId ?? GetDeviceId());
                    command.Parameters.AddWithValue("@isSynced", isSynced ? 1 : 0);

                    var result = await command.ExecuteNonQueryAsync();
                    System.Diagnostics.Debug.WriteLine($"Offline attendance saved successfully. Rows affected: {result}");
                }
                
                // Cache the student name if provided, otherwise try to fetch/find it
                if (string.IsNullOrEmpty(studentName))
                {
                    studentName = await GetStudentNameForDisplayAsync(studentId, teacherId);
                }
                
                // Only cache real names (not "Student X")
                if (!string.IsNullOrEmpty(studentName) && !studentName.StartsWith("Student "))
                {
                    await CacheStudentNameAsync(studentId, studentName);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving offline attendance: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                
                // If it's a readonly database error, try to fix it automatically
                if (ex.Message.Contains("readonly database") || ex.Message.Contains("attempt to write a readonly database"))
                {
                    System.Diagnostics.Debug.WriteLine("Detected readonly database error, attempting automatic fix...");
                    try
                    {
                        // Delete the problematic database file
                        if (File.Exists(_databasePath))
                        {
                            File.Delete(_databasePath);
                            System.Diagnostics.Debug.WriteLine("Deleted problematic database file");
                        }
                        
                        // Reinitialize the database
                        InitializeDatabase();
                        System.Diagnostics.Debug.WriteLine("Database reinitialized successfully");
                        
                        // Try the save operation again
                        return await SaveOfflineAttendanceAsync(studentId, attendanceType, deviceId, scanTime, isSynced, studentName, teacherId);
                    }
                    catch (Exception fixEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in automatic fix: {fixEx.Message}");
                        return false;
                    }
                }
                
                return false;
            }
        }

        private async Task TryCacheStudentNameAsync(string studentId)
        {
            try
            {
                // Check if student name is already cached
                var existingName = await GetStudentNameAsync(studentId);
                if (!string.IsNullOrEmpty(existingName) && !existingName.StartsWith("Student "))
                {
                    return; // Already cached
                }
                
                // Try to get from offline users table
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var command = new SqliteCommand(
                    "SELECT username FROM offline_users WHERE id = @studentId",
                    connection);
                command.Parameters.AddWithValue("@studentId", studentId);
                
                var offlineName = await command.ExecuteScalarAsync();
                if (offlineName != null)
                {
                    // Cache the name
                    await CacheStudentNameAsync(studentId, offlineName.ToString());
                    System.Diagnostics.Debug.WriteLine($"Cached student name: {studentId} -> {offlineName}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error caching student name: {ex.Message}");
            }
        }

        // Get unsynced daily attendance records with their actual stored TimeIn/TimeOut values
        // Get unsynced daily attendance records with their actual stored TimeIn/TimeOut values
        public async Task<List<OfflineDailyAttendanceRecord>> GetUnsyncedDailyAttendanceAsync(string? teacherId = null, string? date = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== Getting Unsynced Daily Attendance Records for {teacherId ?? "All Teachers"} (Date: {date ?? "All"}) ===");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var query = @"
                    SELECT 
                        oda.attendance_id, 
                        oda.student_id, 
                        oda.date, 
                        oda.time_in, 
                        oda.time_out, 
                        oda.status, 
                        oda.device_id, 
                        oda.is_synced, 
                        oda.attendance_type, 
                        oda.created_at,
                        snc.student_name
                    FROM offline_daily_attendance oda
                    LEFT JOIN student_names_cache snc ON oda.student_id = snc.student_id
                    WHERE oda.is_synced IN (0, 2)";

                if (!string.IsNullOrEmpty(teacherId))
                {
                    query += " AND oda.teacher_id = @teacherId";
                }

                if (!string.IsNullOrEmpty(date))
                {
                    query += " AND oda.date = @date";
                }

                query += " ORDER BY oda.created_at DESC";
                
                var records = new List<OfflineDailyAttendanceRecord>();
                
                using var command = new SqliteCommand(query, connection);
                if (!string.IsNullOrEmpty(teacherId))
                {
                    command.Parameters.AddWithValue("@teacherId", teacherId);
                }
                if (!string.IsNullOrEmpty(date))
                {
                    command.Parameters.AddWithValue("@date", date);
                }
                using var reader = await command.ExecuteReaderAsync();
                
                // Pre-load unknown IDs for this teacher to ensure sequential numbering in the list
                var unknownIds = await GetUnknownStudentIdsAsync(teacherId);

                while (await reader.ReadAsync())
                {
                    var studentId = reader.GetString("student_id");
                    var cachedName = reader.IsDBNull("student_name") ? "" : reader.GetString("student_name");
                    var studentName = cachedName;
                    
                    // If no real name, use "Student X" numbering
                    if (string.IsNullOrEmpty(studentName) || studentName.StartsWith("Student "))
                    {
                        var index = unknownIds.IndexOf(studentId);
                        studentName = $"Student {(index == -1 ? unknownIds.Count + 1 : index + 1)}";
                    }

                    records.Add(new OfflineDailyAttendanceRecord
                    {
                        AttendanceId = reader.GetString("attendance_id"),
                        StudentId = studentId,
                        StudentName = studentName,
                        Date = DateTime.Parse(reader.GetString("date")),
                        TimeIn = reader.IsDBNull("time_in") ? null : reader.GetString("time_in"),
                        TimeOut = reader.IsDBNull("time_out") ? null : reader.GetString("time_out"),
                        Status = reader.GetString("status"),
                        DeviceId = reader.IsDBNull("device_id") ? "" : reader.GetString("device_id"),
                        IsSynced = reader.GetInt32("is_synced") == 1,
                        AttendanceType = reader.GetString("attendance_type"),
                        CreatedAt = reader.GetDateTime("created_at")
                    });
                }
                return records;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting unsynced daily attendance records: {ex.Message}");
                return new List<OfflineDailyAttendanceRecord>();
            }
        }


        // Get daily attendance records for a specific student for a specific date (optimized duplicate prevention)
        public async Task<List<OfflineDailyAttendanceRecord>> GetDailyAttendanceForStudentAsync(string studentId, DateTime date, string? teacherId = null)
        {
            try
            {
                var dateStr = date.ToString("yyyy-MM-dd");
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var query = @"
                    SELECT attendance_id, student_id, date, time_in, time_out, status, device_id, is_synced, attendance_type, created_at
                    FROM offline_daily_attendance 
                    WHERE substr(date, 1, 10) = @date AND student_id = @studentId";

                if (!string.IsNullOrEmpty(teacherId))
                {
                    query += " AND teacher_id = @teacherId";
                }

                query += " ORDER BY created_at DESC";
                
                var records = new List<OfflineDailyAttendanceRecord>();
                using var command = new SqliteCommand(query, connection);
                command.Parameters.AddWithValue("@date", dateStr);
                command.Parameters.AddWithValue("@studentId", studentId);
                
                if (!string.IsNullOrEmpty(teacherId))
                {
                    command.Parameters.AddWithValue("@teacherId", teacherId);
                }
                
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    records.Add(new OfflineDailyAttendanceRecord
                    {
                        AttendanceId = reader.GetString("attendance_id"),
                        StudentId = reader.GetString("student_id"),
                        Date = DateTime.Parse(reader.GetString("date")),
                        TimeIn = reader.IsDBNull("time_in") ? null : reader.GetString("time_in"),
                        TimeOut = reader.IsDBNull("time_out") ? null : reader.GetString("time_out"),
                        Status = reader.GetString("status"),
                        DeviceId = reader.IsDBNull("device_id") ? "" : reader.GetString("device_id"),
                        IsSynced = reader.GetInt32("is_synced") == 1,
                        AttendanceType = reader.GetString("attendance_type"),
                        CreatedAt = reader.GetDateTime("created_at")
                    });
                }
                return records;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting daily attendance for student: {ex.Message}");
                return new List<OfflineDailyAttendanceRecord>();
            }
        }

        // Get ALL daily attendance records for a specific date (used for duplicate prevention)
        public async Task<List<OfflineDailyAttendanceRecord>> GetAllDailyAttendanceForDateAsync(DateTime date, string? teacherId = null)
        {
            try
            {
                var dateStr = date.ToString("yyyy-MM-dd");
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var query = @"
                    SELECT attendance_id, student_id, date, time_in, time_out, status, device_id, is_synced, attendance_type, created_at
                    FROM offline_daily_attendance 
                    WHERE date = @date";

                if (!string.IsNullOrEmpty(teacherId))
                {
                    query += " AND teacher_id = @teacherId";
                }

                query += " ORDER BY created_at DESC";
                
                var records = new List<OfflineDailyAttendanceRecord>();
                using var command = new SqliteCommand(query, connection);
                command.Parameters.AddWithValue("@date", dateStr);
                if (!string.IsNullOrEmpty(teacherId))
                {
                    command.Parameters.AddWithValue("@teacherId", teacherId);
                }
                
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    records.Add(new OfflineDailyAttendanceRecord
                    {
                        AttendanceId = reader.GetString("attendance_id"),
                        StudentId = reader.GetString("student_id"),
                        Date = DateTime.Parse(reader.GetString("date")),
                        TimeIn = reader.IsDBNull("time_in") ? null : reader.GetString("time_in"),
                        TimeOut = reader.IsDBNull("time_out") ? null : reader.GetString("time_out"),
                        Status = reader.GetString("status"),
                        DeviceId = reader.IsDBNull("device_id") ? "" : reader.GetString("device_id"),
                        IsSynced = reader.GetInt32("is_synced") == 1,
                        AttendanceType = reader.GetString("attendance_type"),
                        CreatedAt = reader.GetDateTime("created_at")
                    });
                }
                return records;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting all daily attendance records: {ex.Message}");
                return new List<OfflineDailyAttendanceRecord>();
            }
        }

        public async Task<List<OfflineAttendanceRecord>> GetUnsyncedAttendanceAsync(string? teacherId = null)
        {
            var records = new List<OfflineAttendanceRecord>();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var dailyQuery = @"SELECT a.attendance_id, a.student_id, a.date, a.time_in, a.time_out, a.status, a.device_id, a.is_synced, a.created_at, a.attendance_type, a.remarks,
                             COALESCE(n.student_name, '') as student_name
                      FROM offline_daily_attendance a
                      LEFT JOIN student_names_cache n ON a.student_id = n.student_id
                      WHERE a.is_synced IN (0, 2)";

                if (!string.IsNullOrEmpty(teacherId))
                {
                    dailyQuery += " AND a.teacher_id = @teacherId";
                }

                dailyQuery += " ORDER BY a.created_at";

                var dailyCommand = new SqliteCommand(dailyQuery, connection);
                if (!string.IsNullOrEmpty(teacherId))
                {
                    dailyCommand.Parameters.AddWithValue("@teacherId", teacherId);
                }

                // Debug: Log what's in the database
                System.Diagnostics.Debug.WriteLine("=== DEBUGGING DATABASE RECORDS ===");
                var debugCommand = new SqliteCommand(
                    @"SELECT attendance_id, student_id, date, time_in, time_out, device_id, is_synced 
                      FROM offline_daily_attendance 
                      ORDER BY created_at",
                    connection);
                
                using var debugReader = await debugCommand.ExecuteReaderAsync();
                while (await debugReader.ReadAsync())
                {
                    var debugAttendanceId = debugReader.GetString("attendance_id");
                    var debugStudentId = debugReader.GetString("student_id");
                    var debugDate = debugReader.GetString("date");
                    var debugTimeIn = debugReader.IsDBNull("time_in") ? "NULL" : debugReader.GetString("time_in");
                    var debugTimeOut = debugReader.IsDBNull("time_out") ? "NULL" : debugReader.GetString("time_out");
                    var debugDeviceId = debugReader.GetString("device_id");
                    var debugIsSynced = debugReader.GetInt32("is_synced");
                    
                    System.Diagnostics.Debug.WriteLine($"DB Record: ID={debugAttendanceId}, Student={debugStudentId}, Date={debugDate}, TimeIn={debugTimeIn}, TimeOut={debugTimeOut}, DeviceId={debugDeviceId}, Synced={debugIsSynced}");
                }
                debugReader.Close();
                System.Diagnostics.Debug.WriteLine("=== END DATABASE RECORDS DEBUG ===");

                // Pre-load unknown IDs for this teacher
                var unknownIds = await GetUnknownStudentIdsAsync(teacherId);

                using var dailyReader = await dailyCommand.ExecuteReaderAsync();
                while (await dailyReader.ReadAsync())
                {
                    var date = DateTime.Parse(dailyReader.GetString("date"));
                    var studentId = dailyReader.GetString("student_id");
                    var attendanceId = dailyReader.GetString("attendance_id");
                    var deviceId = dailyReader.IsDBNull("device_id") ? "" : dailyReader.GetString("device_id");
                    var isSynced = dailyReader.GetInt32("is_synced") == 1;
                    var createdAt = dailyReader.GetDateTime("created_at");
                    var attendanceType = dailyReader.IsDBNull("attendance_type") ? "Unknown" : dailyReader.GetString("attendance_type");
                    
                    var cachedName = dailyReader.IsDBNull(dailyReader.GetOrdinal("student_name")) ? "" : dailyReader.GetString("student_name");
                    var studentName = cachedName;
                    
                    if (string.IsNullOrEmpty(studentName) || studentName.StartsWith("Student "))
                    {
                        var index = unknownIds.IndexOf(studentId);
                        studentName = $"Student {(index == -1 ? unknownIds.Count + 1 : index + 1)}";
                    }

                    // Create one record per attendance type (TimeIn or TimeOut)
                    // Since we now create separate records for each scan, we only need one record per row
                    DateTime scanTime;
                    if (attendanceType == "TimeIn" && !dailyReader.IsDBNull("time_in"))
                    {
                        var timeIn = TimeSpan.Parse(dailyReader.GetString("time_in"));
                        scanTime = date.Add(timeIn);
                    }
                    else if (attendanceType == "TimeOut" && !dailyReader.IsDBNull("time_out"))
                    {
                        var timeOut = TimeSpan.Parse(dailyReader.GetString("time_out"));
                        scanTime = date.Add(timeOut);
                    }
                    else
                    {
                        // Fallback to current time if no time found
                        scanTime = DateTime.Now;
                    }
                    
                    records.Add(new OfflineAttendanceRecord
                    {
                        Id = attendanceId,
                        StudentId = studentId,
                        StudentName = studentName,
                        ScanTime = scanTime,
                        AttendanceType = attendanceType,
                        IsSynced = dailyReader.GetInt32("is_synced") == 1,
                        SyncStatus = dailyReader.GetInt32("is_synced"),
                        Remarks = dailyReader.IsDBNull("remarks") ? "" : dailyReader.GetString("remarks"),
                        DeviceId = deviceId,
                        CreatedAt = createdAt
                    });
                }
                dailyReader.Close();

                // Get unsynced regular attendance records
                var regularCommand = new SqliteCommand(
                    @"SELECT a.attendance_id, a.student_id, a.timestamp, a.attendance_type, a.device_id, a.is_synced, a.created_at, a.remarks,
                             COALESCE(n.student_name, '') as student_name
                      FROM offline_attendance a
                      LEFT JOIN student_names_cache n ON a.student_id = n.student_id
                      WHERE a.is_synced IN (0, 2) ORDER BY a.created_at",
                    connection);

                using var regularReader = await regularCommand.ExecuteReaderAsync();
                while (await regularReader.ReadAsync())
                {
                    var studentId = regularReader.GetString("student_id");
                    var cachedName = regularReader.IsDBNull(regularReader.GetOrdinal("student_name")) ? "" : regularReader.GetString("student_name");
                    var studentName = cachedName;
                    
                    if (string.IsNullOrEmpty(studentName) || studentName.StartsWith("Student "))
                    {
                        var index = unknownIds.IndexOf(studentId);
                        studentName = $"Student {(index == -1 ? unknownIds.Count + 1 : index + 1)}";
                    }

                    records.Add(new OfflineAttendanceRecord
                    {
                        Id = regularReader.GetString("attendance_id"),
                        StudentId = studentId,
                        StudentName = studentName,
                        AttendanceType = regularReader.GetString("attendance_type"),
                        ScanTime = regularReader.GetDateTime("timestamp"),
                        DeviceId = regularReader.IsDBNull("device_id") ? "" : regularReader.GetString("device_id"),
                        IsSynced = regularReader.GetInt32("is_synced") == 1,
                        SyncStatus = regularReader.GetInt32("is_synced"),
                        Remarks = regularReader.IsDBNull("remarks") ? "" : regularReader.GetString("remarks"),
                        CreatedAt = regularReader.GetDateTime("created_at")
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting unsynced attendance: {ex.Message}");
            }

            return records;
        }

        public async Task<bool> MarkAttendanceAsSyncedAsync(string recordId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Per user's specific request: "pag na sync ko na remove agad"
                // We revert to deleting the record once successfully synced to the server.
                var dailyCommand = new SqliteCommand(
                    "DELETE FROM offline_daily_attendance WHERE attendance_id = @id",
                    connection);
                dailyCommand.Parameters.AddWithValue("@id", recordId);

                var dailyResult = await dailyCommand.ExecuteNonQueryAsync();

                // Try to delete from regular attendance table as well
                var regularCommand = new SqliteCommand(
                    "DELETE FROM offline_attendance WHERE attendance_id = @id",
                    connection);
                regularCommand.Parameters.AddWithValue("@id", recordId);

                var regularResult = await regularCommand.ExecuteNonQueryAsync();
                return (dailyResult > 0 || regularResult > 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing synced attendance: {ex.Message}");
                return false;
            }
        }

        public async Task<int> GetUnsyncedCountAsync(string? teacherId = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Getting unsynced count from SQLite...");
                System.Diagnostics.Debug.WriteLine($"Database path: {_databasePath}");
                System.Diagnostics.Debug.WriteLine($"Database exists: {File.Exists(_databasePath)}");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                System.Diagnostics.Debug.WriteLine("Database connection opened successfully");

                // First, let's check if tables exist
                var tableCheckCommand = new SqliteCommand(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('offline_daily_attendance', 'offline_attendance')",
                    connection);
                
                var tables = new List<string>();
                using var reader = await tableCheckCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
                
                System.Diagnostics.Debug.WriteLine($"Found tables: {string.Join(", ", tables)}");

                // Debug: Check what's actually in the database
                var debugCommand = new SqliteCommand("SELECT COUNT(*) FROM offline_daily_attendance", connection);
                var totalRecords = await debugCommand.ExecuteScalarAsync();
                System.Diagnostics.Debug.WriteLine($"Total records in offline_daily_attendance: {totalRecords}");
                
                var debugCommand2 = new SqliteCommand("SELECT COUNT(*) FROM offline_daily_attendance WHERE is_synced = 0", connection);
                var unsyncedRecords = await debugCommand2.ExecuteScalarAsync();
                System.Diagnostics.Debug.WriteLine($"Unsynced records (no device_id filter): {unsyncedRecords}");
                
                var debugCommand3 = new SqliteCommand("SELECT COUNT(*) FROM offline_daily_attendance WHERE device_id IS NOT NULL", connection);
                var recordsWithDeviceId = await debugCommand3.ExecuteScalarAsync();
                System.Diagnostics.Debug.WriteLine($"Records with device_id: {recordsWithDeviceId}");
                
                // Count individual daily attendance records (separate TimeIn/TimeOut records)
                // Count UNIQUE students instead of raw records
                var dailyQuery = @"SELECT COUNT(DISTINCT student_id) 
                      FROM offline_daily_attendance 
                      WHERE is_synced IN (0, 2)";
                if (!string.IsNullOrEmpty(teacherId))
                {
                    dailyQuery += " AND teacher_id = @teacherId";
                }
                var dailyCountCommand = new SqliteCommand(dailyQuery, connection);
                if (!string.IsNullOrEmpty(teacherId))
                {
                    dailyCountCommand.Parameters.AddWithValue("@teacherId", teacherId);
                }
                var dailyCount = Convert.ToInt32(await dailyCountCommand.ExecuteScalarAsync());
                System.Diagnostics.Debug.WriteLine($"Unique students pending daily attendance: {dailyCount}");

                // Count other unsynced attendance records
                var otherQuery = @"SELECT COUNT(DISTINCT student_id) 
                      FROM offline_attendance 
                      WHERE is_synced IN (0, 2)";
                if (!string.IsNullOrEmpty(teacherId))
                {
                    otherQuery += " AND teacher_id = @teacherId";
                }
                var otherCountCommand = new SqliteCommand(otherQuery, connection);
                if (!string.IsNullOrEmpty(teacherId))
                {
                    otherCountCommand.Parameters.AddWithValue("@teacherId", teacherId);
                }
                var otherCount = Convert.ToInt32(await otherCountCommand.ExecuteScalarAsync());
                System.Diagnostics.Debug.WriteLine($"Other unsynced attendance count: {otherCount}");

                // For total unique students, we would ideally do a UNION, but for now max is safe
                var totalCount = Math.Max(dailyCount, otherCount);
                System.Diagnostics.Debug.WriteLine($"Unsynced unique student count result: {totalCount}");
                return totalCount;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting unsynced count: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return 0;
            }
        }

        public async Task<List<PendingStudent>> GetPendingStudentsAsync(string? teacherId = null)
        {
            var pendingStudents = new List<PendingStudent>();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var query = @"
                    SELECT a.attendance_id, a.student_id, a.date, a.time_in, a.time_out, a.attendance_type, a.created_at, a.device_id,
                           COALESCE(n.student_name, '') as student_name
                    FROM offline_daily_attendance a
                    LEFT JOIN student_names_cache n ON a.student_id = n.student_id
                    WHERE a.is_synced = 0";

                if (!string.IsNullOrEmpty(teacherId))
                {
                    query += " AND a.teacher_id = @teacherId";
                }

                query += " ORDER BY a.created_at DESC";
                
                var records = new List<OfflineDailyAttendanceRecord>();
                using var command = new SqliteCommand(query, connection);
                if (!string.IsNullOrEmpty(teacherId))
                {
                    command.Parameters.AddWithValue("@teacherId", teacherId);
                }
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var attendanceId = reader.GetString("attendance_id");
                    var studentId = reader.GetString("student_id");
                    var date = DateTime.Parse(reader.GetString("date"));
                    var attendanceType = reader.GetString("attendance_type");
                    var createdAt = reader.GetDateTime("created_at");
                    var deviceId = reader.IsDBNull("device_id") ? "" : reader.GetString("device_id");
                    
                    // Determine scan time based on attendance type
                    DateTime scanTime;
                    if (attendanceType == "TimeIn" && !reader.IsDBNull("time_in"))
                    {
                        var timeIn = TimeSpan.Parse(reader.GetString("time_in"));
                        scanTime = date.Add(timeIn);
                    }
                    else if (attendanceType == "TimeOut" && !reader.IsDBNull("time_out"))
                    {
                        var timeOut = TimeSpan.Parse(reader.GetString("time_out"));
                        scanTime = date.Add(timeOut);
                    }
                    else
                    {
                        scanTime = createdAt; // Fallback to creation time
                    }

                    // Try to get student name from cache
                    var studentName = reader.GetString("student_name");
                    
                    // If no name found, try to get from offline users table as fallback
                    if (string.IsNullOrEmpty(studentName) || studentName.StartsWith("Student "))
                    {
                        try
                        {
                            using var nameConnection = new SqliteConnection(_connectionString);
                            await nameConnection.OpenAsync();
                            
                            var nameCommand = new SqliteCommand(
                                "SELECT username FROM offline_users WHERE id = @studentId",
                                nameConnection);
                            nameCommand.Parameters.AddWithValue("@studentId", studentId);
                            
                            var offlineName = await nameCommand.ExecuteScalarAsync();
                            if (offlineName != null)
                            {
                                studentName = offlineName.ToString();
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error getting offline name: {ex.Message}");
                        }
                    }

                    if (string.IsNullOrEmpty(studentName))
                    {
                        studentName = await GetStudentNameForDisplayAsync(studentId, teacherId);
                    }

                    pendingStudents.Add(new PendingStudent
                    {
                        StudentId = studentId,
                        StudentName = studentName,
                        AttendanceType = attendanceType,
                        ScanTime = scanTime,
                        DeviceId = deviceId,
                        RecordId = attendanceId,
                        CreatedAt = createdAt
                    });
                }
                reader.Close();

                // Get other offline attendance records (subject-based attendance)
                var otherQuery = @"SELECT attendance_id, student_id, timestamp, attendance_type, device_id, created_at
                      FROM offline_attendance 
                      WHERE is_synced = 0";
                if (!string.IsNullOrEmpty(teacherId))
                {
                    otherQuery += " AND teacher_id = @teacherId";
                }
                otherQuery += " ORDER BY created_at";

                var otherCommand = new SqliteCommand(otherQuery, connection);
                if (!string.IsNullOrEmpty(teacherId))
                {
                    otherCommand.Parameters.AddWithValue("@teacherId", teacherId);
                }
                
                using var otherReader = await otherCommand.ExecuteReaderAsync();
                while (await otherReader.ReadAsync())
                {
                    var attendanceId = otherReader.GetString("attendance_id");
                    var studentId = otherReader.GetString("student_id");
                    var timestamp = otherReader.GetDateTime("timestamp");
                    var attendanceType = otherReader.GetString("attendance_type");
                    var deviceId = otherReader.IsDBNull("device_id") ? "" : otherReader.GetString("device_id");
                    var createdAt = otherReader.GetDateTime("created_at");
                    
                    // Try to get student name from cache
                    var studentName = await GetStudentNameAsync(studentId);
                    
                    if (string.IsNullOrEmpty(studentName))
                    {
                        studentName = await GetStudentNameForDisplayAsync(studentId, teacherId);
                    }
                    
                    pendingStudents.Add(new PendingStudent
                    {
                        StudentId = studentId,
                        StudentName = studentName,
                        AttendanceType = attendanceType,
                        ScanTime = timestamp,
                        DeviceId = deviceId,
                        RecordId = attendanceId,
                        CreatedAt = createdAt
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting pending students: {ex.Message}");
            }
            return pendingStudents.OrderBy(p => p.CreatedAt).ToList();
        }

        private async Task<string?> GetStudentNameAsync(string studentId)
        {
            try
            {
                // First try to get from local cache
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Create cache table if it doesn't exist first
                var createTableCommand = new SqliteCommand(
                    @"CREATE TABLE IF NOT EXISTS student_names_cache (
                        student_id TEXT PRIMARY KEY,
                        student_name TEXT NOT NULL,
                        grade_level INTEGER,
                        section TEXT,
                        strand TEXT,
                        school_id TEXT,
                        cached_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    )", connection);
                await createTableCommand.ExecuteNonQueryAsync();

                var command = new SqliteCommand(
                    "SELECT student_name FROM student_names_cache WHERE student_id = @studentId",
                    connection);
                command.Parameters.AddWithValue("@studentId", studentId);

                var cachedName = await command.ExecuteScalarAsync();
                if (cachedName != null)
                {
                    return cachedName.ToString();
                }

                // If not in cache, try to fetch from server
                return await FetchStudentNameFromServerAsync(studentId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting student name: {ex.Message}");
                return null;
            }
        }

        public async Task<Student?> GetStudentProfileAsync(string studentId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var command = new SqliteCommand(
                    "SELECT student_id, student_name, grade_level, section, strand, school_id FROM student_names_cache WHERE student_id = @studentId",
                    connection);
                command.Parameters.AddWithValue("@studentId", studentId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new Student
                    {
                        StudentId = reader.GetString(0),
                        FullName = reader.GetString(1),
                        GradeLevel = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                        Section = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        Strand = reader.IsDBNull(4) ? "" : reader.GetString(4),
                        SchoolId = reader.IsDBNull(5) ? "" : reader.GetString(5)
                    };
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting student profile: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> IsStudentInTeacherClassOfflineAsync(string studentId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var command = new SqliteCommand(
                    "SELECT COUNT(*) FROM student_names_cache WHERE student_id = @studentId",
                    connection);
                command.Parameters.AddWithValue("@studentId", studentId);

                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking student class offline: {ex.Message}");
                return false;
            }
        }

        // Public method to get student name (for UI)
        public async Task<string> GetStudentNameForDisplayAsync(string studentId, string? teacherId = null)
        {
            try
            {
                // First try to get from student names cache
                var name = await GetStudentNameAsync(studentId);
                
                // If we got a real name (not "Student {ID}"), return it
                if (!string.IsNullOrEmpty(name) && !name.StartsWith("Student "))
                {
                    return name;
                }
                
                // Fallback: try to get from offline users table
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var command = new SqliteCommand(
                    "SELECT username FROM offline_users WHERE id = @studentId",
                    connection);
                command.Parameters.AddWithValue("@studentId", studentId);
                
                var offlineName = await command.ExecuteScalarAsync();
                if (offlineName != null)
                {
                    var nameStr = offlineName.ToString();
                    if (!string.IsNullOrEmpty(nameStr)) return nameStr;
                }
                
                // If still no name found, return "Student X"
                var unknownIndex = await GetUnknownStudentIndexAsync(studentId, teacherId);
                return $"Student {unknownIndex}"; 
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting student name for display: {ex.Message}");
                return "Student";
            }
        }

        public async Task<List<string>> GetUnknownStudentIdsAsync(string? teacherId = null)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Get all unique student IDs in the unsynced table that are NOT in the cache (or are placeholder "Student " entries)
                var queryStr = @"
                    SELECT student_id FROM (
                        SELECT student_id, teacher_id, created_at FROM offline_attendance 
                        WHERE is_synced IN (0, 2) 
                          AND (student_id NOT IN (SELECT student_id FROM student_names_cache)
                               OR student_id IN (SELECT student_id FROM student_names_cache WHERE student_name LIKE 'Student %'))
                        UNION ALL
                        SELECT student_id, teacher_id, created_at FROM offline_daily_attendance
                        WHERE is_synced IN (0, 2) 
                          AND (student_id NOT IN (SELECT student_id FROM student_names_cache)
                               OR student_id IN (SELECT student_id FROM student_names_cache WHERE student_name LIKE 'Student %'))
                    ) 
                    WHERE (@teacherId IS NULL OR teacher_id = @teacherId OR teacher_id = 'Unknown' OR teacher_id = 'Offline')
                    GROUP BY student_id 
                    ORDER BY MIN(created_at) ASC";

                var unknownIds = new List<string>();
                using var command = new SqliteCommand(queryStr, connection);
                command.Parameters.AddWithValue("@teacherId", (object?)teacherId ?? DBNull.Value);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    unknownIds.Add(reader.GetString(0));
                }
                return unknownIds;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting unknown student IDs: {ex.Message}");
                return new List<string>();
            }
        }

        private async Task<int> GetUnknownStudentIndexAsync(string studentId, string? teacherId = null)
        {
            var unknownIds = await GetUnknownStudentIdsAsync(teacherId);
            var index = unknownIds.IndexOf(studentId);
            if (index != -1) return index + 1;
            return unknownIds.Count + 1;
        }

        private async Task<string?> FetchStudentNameFromServerAsync(string studentId)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.BaseAddress = new Uri("https://attrack-sr9l.onrender.com/");
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                // Try to get student info from your API
                var response = await httpClient.GetAsync($"api/student/{studentId}");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Student API response: {json}");
                    
                    // Parse the JSON to get student name
                    // Your API returns: {"studentId": "123", "fullName": "John Doe", ...}
                    var studentData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                    
                    if (studentData != null && studentData.ContainsKey("fullName"))
                    {
                        var studentName = studentData["fullName"].ToString();
                        
                        // Cache the name locally for future use
                        await CacheStudentNameAsync(studentId, studentName);
                        
                        return studentName;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Student API error: {response.StatusCode} - {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching student name from server: {ex.Message}");
            }
            
            return null;
        }

        public async Task CacheStudentNameAsync(string studentId, string studentName)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Create cache table if it doesn't exist
                var createTableCommand = new SqliteCommand(
                    @"CREATE TABLE IF NOT EXISTS student_names_cache (
                        student_id TEXT PRIMARY KEY,
                        student_name TEXT NOT NULL,
                        grade_level INTEGER,
                        section TEXT,
                        strand TEXT,
                        school_id TEXT,
                        cached_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    )", connection);
                await createTableCommand.ExecuteNonQueryAsync();

                // Insert or replace the cached name and profile
                var command = new SqliteCommand(
                    @"INSERT OR REPLACE INTO student_names_cache (student_id, student_name, grade_level, section, strand, school_id, cached_at) 
                      VALUES (@studentId, @studentName, @gradeLevel, @section, @strand, @schoolId, @cachedAt)",
                    connection);
                command.Parameters.AddWithValue("@studentId", studentId);
                command.Parameters.AddWithValue("@studentName", studentName);
                // Note: This method currently only takes ID and Name, so we'll use nulls for the rest
                // unless we update the signature. For now, keep as null to avoid breaking other calls.
                command.Parameters.AddWithValue("@gradeLevel", DBNull.Value);
                command.Parameters.AddWithValue("@section", DBNull.Value);
                command.Parameters.AddWithValue("@strand", DBNull.Value);
                command.Parameters.AddWithValue("@schoolId", DBNull.Value);
                command.Parameters.AddWithValue("@cachedAt", DateTime.Now);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error caching student name: {ex.Message}");
            }
        }

        // Bulk download all students for a teacher when they log in
        public async Task<bool> DownloadAllStudentsForTeacherAsync(string teacherId, string apiBaseUrl)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Starting bulk download of students for teacher: {teacherId}");

                using var httpClient = new HttpClient();
                httpClient.BaseAddress = new Uri(apiBaseUrl);
                httpClient.Timeout = TimeSpan.FromSeconds(60); // Longer timeout for bulk download

                // Get all students assigned to this teacher
                var response = await httpClient.GetAsync($"api/teacher/{teacherId}/students");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Students API response: {json}");
                    
                    // Parse the JSON array of students
                    var students = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
                    
                    if (students != null && students.Any())
                    {
                        // Clear existing cache and bulk insert
                        await ClearStudentCacheAsync();
                        await BulkCacheStudentsAsync(students);
                        
                        System.Diagnostics.Debug.WriteLine($"Successfully cached {students.Count} students for teacher {teacherId}");
                        return true;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Students API error: {response.StatusCode} - {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error downloading students for teacher: {ex.Message}");
            }
            
            return false;
        }



        private async Task ClearStudentCacheAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var command = new SqliteCommand("DELETE FROM student_names_cache", connection);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing student cache: {ex.Message}");
            }
        }

        private async Task BulkCacheStudentsAsync(List<Dictionary<string, object>> students)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Create cache table if it doesn't exist
                var createTableCommand = new SqliteCommand(
                    @"CREATE TABLE IF NOT EXISTS student_names_cache (
                        student_id TEXT PRIMARY KEY,
                        student_name TEXT NOT NULL,
                        grade_level INTEGER,
                        section TEXT,
                        strand TEXT,
                        school_id TEXT,
                        cached_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    )", connection);
                await createTableCommand.ExecuteNonQueryAsync();

                // Bulk insert all students with full profile
                using var transaction = connection.BeginTransaction();
                var command = new SqliteCommand(
                    @"INSERT OR REPLACE INTO student_names_cache (student_id, student_name, grade_level, section, strand, school_id, cached_at) 
                      VALUES (@studentId, @studentName, @gradeLevel, @section, @strand, @schoolId, @cachedAt)",
                    connection, transaction);

                foreach (var student in students)
                {
                    if (student.ContainsKey("studentId") && student.ContainsKey("fullName"))
                    {
                        command.Parameters.Clear();
                        command.Parameters.AddWithValue("@studentId", student["studentId"]?.ToString() ?? "");
                        command.Parameters.AddWithValue("@studentName", student["fullName"]?.ToString() ?? "");
                        
                        // New fields
                        command.Parameters.AddWithValue("@gradeLevel", student.ContainsKey("gradeLevel") ? student["gradeLevel"] : DBNull.Value);
                        command.Parameters.AddWithValue("@section", student.ContainsKey("section") ? student["section"]?.ToString() : DBNull.Value);
                        command.Parameters.AddWithValue("@strand", student.ContainsKey("strand") ? student["strand"]?.ToString() : DBNull.Value);
                        command.Parameters.AddWithValue("@schoolId", student.ContainsKey("schoolId") ? student["schoolId"]?.ToString() : DBNull.Value);
                        
                        command.Parameters.AddWithValue("@cachedAt", DateTime.Now);
                        await command.ExecuteNonQueryAsync();
                    }
                }

                transaction.Commit();
                System.Diagnostics.Debug.WriteLine($"Bulk cached {students.Count} students successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error bulk caching students: {ex.Message}");
            }
        }

        public async Task<SyncResult> SyncIndividualStudentAsync(string studentId, string apiBaseUrl, string teacherId)
        {
            try
            {
                var studentRecords = await GetUnsyncedAttendanceAsync();
                var studentSpecificRecords = studentRecords.Where(r => r.StudentId == studentId).ToList();
                
                if (!studentSpecificRecords.Any())
                {
                    return new SyncResult { Success = true, Message = "No records to sync" }; // No records to sync
                }

                // First, validate if student is in teacher's class list
                var isValidStudent = await ValidateStudentInTeacherClassAsync(studentId, apiBaseUrl, teacherId);
                if (!isValidStudent)
                {
                    // Remove all records for this invalid student
                    await DeleteOfflineRecordsByStudentIdAsync(studentId);
                    System.Diagnostics.Debug.WriteLine($"Student {studentId} is not in teacher's class list. Removed from pending records.");
                    return new SyncResult 
                    { 
                        Success = false, 
                        Message = $"Student {studentId} is not in your class list and has been removed from pending records.",
                        InvalidStudents = new List<string> { studentId }
                    };
                }

                using var httpClient = new HttpClient();
                httpClient.BaseAddress = new Uri(apiBaseUrl);
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                int successCount = 0;
                int failCount = 0;

                foreach (var record in studentSpecificRecords)
                {
                    try
                    {
                        HttpResponseMessage response;
                        
                        if (record.AttendanceType == "TimeIn")
                        {
                            var request = new
                            {
                                StudentId = record.StudentId,
                                Date = record.ScanTime.Date,
                                TimeIn = record.ScanTime.TimeOfDay
                            };
                            response = await httpClient.PostAsJsonAsync("api/dailyattendance/daily-timein", request);
                        }
                        else
                        {
                            var request = new
                            {
                                StudentId = record.StudentId,
                                Date = record.ScanTime.Date,
                                TimeOut = record.ScanTime.TimeOfDay
                            };
                            response = await httpClient.PostAsJsonAsync("api/dailyattendance/daily-timeout", request);
                        }

                        if (response.IsSuccessStatusCode)
                        {
                            // Mark record as synced
                            await MarkAttendanceAsSyncedAsync(record.Id);
                            successCount++;
                            System.Diagnostics.Debug.WriteLine($"Successfully synced record {record.Id} for student {record.StudentId}");
                        }
                        else
                        {
                            failCount++;
                            System.Diagnostics.Debug.WriteLine($"Failed to sync record {record.Id}: {response.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        System.Diagnostics.Debug.WriteLine($"Error syncing record {record.Id}: {ex.Message}");
                    }
                }

                // Log sync results
                await LogSyncAsync("IndividualSync", studentSpecificRecords.Count, 
                    failCount == 0 ? "Success" : "Partial", 
                    failCount > 0 ? $"{failCount} records failed to sync" : null);

                System.Diagnostics.Debug.WriteLine($"Individual sync completed for {studentId}: {successCount} successful, {failCount} failed");
                return new SyncResult 
                { 
                    Success = failCount == 0, 
                    Message = failCount == 0 ? "Sync completed successfully" : $"{failCount} records failed to sync",
                    SuccessCount = successCount,
                    FailCount = failCount
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in individual sync: {ex.Message}");
                await LogSyncAsync("IndividualSync", 0, "Error", ex.Message);
                return new SyncResult { Success = false, Message = $"Error: {ex.Message}" };
            }
        }

        // Export Methods
        public async Task<string> ExportAttendanceDataAsync()
        {
            try
            {
                var records = await GetUnsyncedAttendanceAsync();
                var csv = new System.Text.StringBuilder();
                
                // CSV Header
                csv.AppendLine("Student ID,Attendance Type,Scan Time,Device ID,Created At");

                // CSV Data
                foreach (var record in records)
                {
                    csv.AppendLine($"{record.StudentId},{record.AttendanceType},{record.ScanTime:yyyy-MM-dd HH:mm:ss},{record.DeviceId},{record.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                }

                return csv.ToString();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error exporting attendance data: {ex.Message}");
                return string.Empty;
            }
        }

        public async Task<bool> SaveExportToFileAsync(string fileName = null)
        {
            try
            {
                var csvData = await ExportAttendanceDataAsync();
                if (string.IsNullOrEmpty(csvData))
                    return false;

                fileName ??= $"attendance_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                
                // Save to AppDataDirectory (guaranteed to work)
                var filePath = Path.Combine(FileSystem.AppDataDirectory, fileName);

                await File.WriteAllTextAsync(filePath, csvData);
                System.Diagnostics.Debug.WriteLine($"Data exported to: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving export file: {ex.Message}");
                return false;
            }
        }

        public async Task<string> SaveExportToDownloadsAsync()
        {
            try
            {
                var csvData = await ExportAttendanceDataAsync();
                if (string.IsNullOrEmpty(csvData))
                    return "❌ No data to export.";

                var fileName = $"attendance_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                
                // Try multiple Downloads folder paths
                var downloadsPaths = new[]
                {
                    "/storage/emulated/0/Download",
                    "/storage/emulated/0/Downloads", 
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Download"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    "/sdcard/Download",
                    "/sdcard/Downloads"
                };

                foreach (var downloadsPath in downloadsPaths)
                {
                    try
                    {
                        if (Directory.Exists(downloadsPath))
                        {
                            var destinationPath = Path.Combine(downloadsPath, fileName);
                            await File.WriteAllTextAsync(destinationPath, csvData);
                            
                            System.Diagnostics.Debug.WriteLine($"Successfully exported CSV to: {destinationPath}");
                            return $"✅ CSV Exported to Downloads folder!\nPath: {destinationPath}\n\nYou can now see '{fileName}' using your File Manager.";
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not export CSV to {downloadsPath}: {ex.Message}");
                    }
                }

                // Fallback to regular save if downloads folder not accessible
                await SaveExportToFileAsync(fileName);
                return $"⚠️ Could not access Downloads folder.\nExported to app's private directory as '{fileName}'.\nCheck Android permissions if you still can't find it.";
            }
            catch (Exception ex)
            {
                return $"❌ Error exporting CSV: {ex.Message}";
            }
        }

        // Utility Methods
        private string GetDeviceId()
        {
            // Generate a simple device ID (in production, use proper device identification)
            return $"DEV_{Environment.MachineName}_{DateTime.Now:yyyyMMdd}";
        }

        public async Task LogSyncAsync(string syncType, int recordCount, string status, string? errorMessage = null)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var command = new SqliteCommand(
                    "INSERT INTO sync_log (sync_type, record_count, status, error_message) VALUES (@syncType, @recordCount, @status, @errorMessage)",
                    connection);
                command.Parameters.AddWithValue("@syncType", syncType);
                command.Parameters.AddWithValue("@recordCount", recordCount);
                command.Parameters.AddWithValue("@status", status);
                command.Parameters.AddWithValue("@errorMessage", errorMessage ?? "");

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error logging sync: {ex.Message}");
            }
        }

        // Method to get database path for display
        public string GetDatabasePath()
        {
            return _databasePath;
        }

        // Method to get app data directory
        public string GetAppDataDirectory()
        {
            return FileSystem.AppDataDirectory;
        }

        // Method to copy database to a more accessible location
        public async Task<string> CopyDatabaseToAccessibleLocationAsync()
        {
            try
            {
                if (!File.Exists(_databasePath))
                {
                    return "Database file does not exist yet. Scan some QR codes first.";
                }

                // Try multiple Downloads folder paths
                var downloadsPaths = new[]
                {
                    "/storage/emulated/0/Download",
                    "/storage/emulated/0/Downloads", 
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Download"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    "/sdcard/Download",
                    "/sdcard/Downloads"
                };

                foreach (var downloadsPath in downloadsPaths)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"Trying Downloads path: {downloadsPath}");
                        
                        if (Directory.Exists(downloadsPath))
                        {
                            var destinationPath = Path.Combine(downloadsPath, "attrak_database_copy.db");
                            File.Copy(_databasePath, destinationPath, true);
                            
                            System.Diagnostics.Debug.WriteLine($"Successfully copied database to: {destinationPath}");
                            return $"✅ Database copied to Downloads folder!\nPath: {destinationPath}\n\nYou can now see 'attrak_database_copy.db' using your File Manager.";
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Directory does not exist: {downloadsPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not copy to {downloadsPath}: {ex.Message}");
                    }
                }

                // If all Downloads paths fail, try Documents
                var documentsPaths = new[]
                {
                    "/storage/emulated/0/Documents",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents"),
                    "/sdcard/Documents"
                };

                foreach (var documentsPath in documentsPaths)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"Trying Documents path: {documentsPath}");
                        
                        if (Directory.Exists(documentsPath))
                        {
                            var destinationPath = Path.Combine(documentsPath, "attrak_database_copy.db");
                            File.Copy(_databasePath, destinationPath, true);
                            
                            System.Diagnostics.Debug.WriteLine($"Successfully copied database to: {destinationPath}");
                            return $"✅ Database copied to Documents!\nPath: {destinationPath}\nCheck your Documents folder in file manager.";
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not copy to {documentsPath}: {ex.Message}");
                    }
                }

                // Final fallback: Create a copy in AppDataDirectory
                try
                {
                    var copyPath = Path.Combine(FileSystem.AppDataDirectory, "attrak_database_copy.db");
                    File.Copy(_databasePath, copyPath, true);
                    return $"⚠️ Could not access Downloads/Documents folders.\nDatabase copied to: {copyPath}\nThis is in app's private directory (not visible in file manager).\nTry enabling 'All files access' permission in Android Settings.";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not create copy: {ex.Message}");
                }

                return "❌ Could not copy database to any accessible location.\nDatabase is stored in app's private directory for security.\nTry enabling storage permissions in Android Settings > Apps > ScannerMaui > Permissions.";
            }
            catch (Exception ex)
            {
                return $"❌ Error copying database: {ex.Message}";
            }
        }

        // Export database using MAUI file sharing
        public async Task<string> ExportDatabaseViaSharingAsync()
        {
            try
            {
                if (!File.Exists(_databasePath))
                {
                    return "Database file does not exist yet. Scan some QR codes first.";
                }

                // Create a copy with timestamp in AppDataDirectory
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var exportPath = Path.Combine(FileSystem.AppDataDirectory, $"attrak_database_{timestamp}.db");
                File.Copy(_databasePath, exportPath, true);

                // Try to use MAUI's file sharing (if available)
                try
                {
                    // This would require implementing file sharing in the UI
                    // For now, just return the path
                    return $"Database exported to: {exportPath}\nUse 'Create Test File' to get more details about accessing this file.";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"File sharing error: {ex.Message}");
                    return $"Database exported to: {exportPath}\nNote: File is in app's private directory.";
                }
            }
            catch (Exception ex)
            {
                return $"Error exporting database: {ex.Message}";
            }
        }

        // Method to validate if a student is in the teacher's class list
        private async Task<bool> ValidateStudentInTeacherClassAsync(string studentId, string apiBaseUrl, string teacherId)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.BaseAddress = new Uri(apiBaseUrl);
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                // Get all students assigned to this teacher
                var response = await httpClient.GetAsync($"api/teacher/{teacherId}/students");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var students = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
                    
                    if (students != null)
                    {
                        // Check if the student ID exists in the teacher's student list
                        var studentExists = students.Any(s => s.ContainsKey("studentId") && s["studentId"].ToString() == studentId);
                        System.Diagnostics.Debug.WriteLine($"Student {studentId} validation result: {studentExists}");
                        return studentExists;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Error validating student {studentId}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error validating student {studentId}: {ex.Message}");
            }
            
            return false; // Default to false if validation fails
        }

        // Method to remove all records for a student from both offline tables (used for individual deletion)
        public async Task<bool> DeleteOfflineRecordsByStudentIdAsync(string studentId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                using var transaction = connection.BeginTransaction();
                try
                {
                    // Remove from daily attendance table
                    var dailyCommand = new SqliteCommand(
                        "DELETE FROM offline_daily_attendance WHERE student_id = @studentId",
                        connection, transaction);
                    dailyCommand.Parameters.AddWithValue("@studentId", studentId);
                    var dailyResult = await dailyCommand.ExecuteNonQueryAsync();

                    // Remove from regular attendance table
                    var regularCommand = new SqliteCommand(
                        "DELETE FROM offline_attendance WHERE student_id = @studentId",
                        connection, transaction);
                    regularCommand.Parameters.AddWithValue("@studentId", studentId);
                    var regularResult = await regularCommand.ExecuteNonQueryAsync();

                    await transaction.CommitAsync();
                    
                    var totalRemoved = dailyResult + regularResult;
                    System.Diagnostics.Debug.WriteLine($"Removed {totalRemoved} records for student {studentId} (daily: {dailyResult}, regular: {regularResult})");
                    return totalRemoved > 0;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing student records: {ex.Message}");
                return false;
            }
        }

        // Auto-sync method to send offline data to API when connection is restored
        public async Task<SyncResult> AutoSyncOfflineDataAsync(string apiBaseUrl, string teacherId, Action<int, int>? progressCallback = null)
        {
            var syncResult = new SyncResult { Success = true, Message = "Sync started" };
            try
            {
                var unsyncedRecords = await GetUnsyncedAttendanceAsync(teacherId);
                if (!unsyncedRecords.Any())
                {
                    System.Diagnostics.Debug.WriteLine("No offline records to sync");
                    return new SyncResult { Success = true, Message = "No offline records to sync" };
                }

                System.Diagnostics.Debug.WriteLine($"Starting auto-sync of {unsyncedRecords.Count} offline records");

                using var httpClient = new HttpClient();
                httpClient.BaseAddress = new Uri(apiBaseUrl);
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                int processed = 0;
                int total = unsyncedRecords.Count;

                foreach (var record in unsyncedRecords)
                {
                    var detail = new SyncRecordDetail
                    {
                        StudentId = record.StudentId,
                        StudentName = record.StudentName,
                        Action = record.AttendanceType,
                        Success = false
                    };

                    try
                    {
                        HttpResponseMessage response;
                        
                        // Use record's stored teacher ID, fallback to the active teacher
                        var effectiveTeacherId = !string.IsNullOrEmpty(record.TeacherId) 
                            ? record.TeacherId 
                            : teacherId;

                        if (record.AttendanceType == "TimeIn")
                        {
                            var request = new DailyTimeInRequest
                            {
                                StudentId = record.StudentId,
                                Date = record.ScanTime.Date,
                                TimeIn = record.ScanTime.TimeOfDay,
                                TeacherId = effectiveTeacherId
                            };
                            response = await httpClient.PostAsJsonAsync("api/dailyattendance/daily-timein", request);
                        }
                        else
                        {
                            var request = new DailyTimeOutRequest
                            {
                                StudentId = record.StudentId,
                                Date = record.ScanTime.Date,
                                TimeOut = record.ScanTime.TimeOfDay,
                                TeacherId = effectiveTeacherId
                            };
                            response = await httpClient.PostAsJsonAsync("api/dailyattendance/daily-timeout", request);
                        }

                        if (response.IsSuccessStatusCode)
                        {
                            await MarkAttendanceAsSyncedAsync(record.Id);
                            detail.Success = true;
                            detail.Message = "Synced Successfully";
                            syncResult.SuccessCount++;
                        }
                        else
                        {
                            var errorContent = await response.Content.ReadAsStringAsync();
                            detail.Message = "Failed: " + errorContent;
                            
                            bool isPermanentRejection = false;

                            // Map specific messages
                            if (errorContent.Contains("TeacherId") && errorContent.Contains("required"))
                            {
                                detail.Message = "No teacher info (re-login and sync again)";
                            }
                            else if (errorContent.Contains("not assigned") || errorContent.Contains("not in your class"))
                            {
                                detail.Message = "Not your student";
                                isPermanentRejection = true;
                            }
                            else if (errorContent.Contains("Section mismatch"))
                            {
                                detail.Message = "Wrong section";
                                isPermanentRejection = true;
                            }
                            else if (errorContent.Contains("already recorded"))
                            {
                                detail.Message = "Double entry (Already Sync)";
                                isPermanentRejection = true;
                            }
                            else if (errorContent.Contains("Grade level mismatch"))
                            {
                                detail.Message = "Not student (Grade Mismatch)";
                                isPermanentRejection = true;
                            }

                            // If the server explicitly rejected it due to rules, mark it as REJECTED (2)
                            // so it stops trying to sync but stays in the DB for the user to see why it failed.
                            if (isPermanentRejection)
                            {
                                try 
                                {
                                    using var conn = new SqliteConnection(_connectionString);
                                    await conn.OpenAsync();
                                    var cmd = new SqliteCommand(
                                        "UPDATE offline_daily_attendance SET is_synced = 2, status = 'Rejected', remarks = @msg, updated_at = CURRENT_TIMESTAMP WHERE attendance_id = @id", 
                                        conn);
                                    cmd.Parameters.AddWithValue("@msg", detail.Message);
                                    cmd.Parameters.AddWithValue("@id", record.Id);
                                    await cmd.ExecuteNonQueryAsync();

                                    var cmd2 = new SqliteCommand(
                                        "UPDATE offline_attendance SET is_synced = 2, remarks = @msg, updated_at = CURRENT_TIMESTAMP WHERE attendance_id = @id", 
                                        conn);
                                    cmd2.Parameters.AddWithValue("@msg", detail.Message);
                                    cmd2.Parameters.AddWithValue("@id", record.Id);
                                    await cmd2.ExecuteNonQueryAsync();
                                }
                                catch(Exception ex) { 
                                    System.Diagnostics.Debug.WriteLine($"Error marking as rejected: {ex.Message}"); 
                                }
                            }

                            syncResult.FailCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        detail.Message = "Error: " + ex.Message;
                        syncResult.FailCount++;
                    }

                    syncResult.Details.Add(detail);
                    processed++;
                    progressCallback?.Invoke(processed, total);
                }

                await LogSyncAsync("AutoSync", total, syncResult.FailCount == 0 ? "Success" : "Partial", syncResult.FailCount > 0 ? $"{syncResult.FailCount} failed" : null);

                syncResult.Success = syncResult.FailCount == 0;
                syncResult.Message = syncResult.FailCount == 0 ? "Sync completed successfully" : $"{syncResult.FailCount} records failed to sync";
                
                return syncResult;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Critical error in auto-sync: {ex.Message}");
                return new SyncResult { Success = false, Message = "Critical sync error: " + ex.Message };
            }
        }

        // Method to create a test database file
        public async Task<bool> CreateTestDatabaseFileAsync()
        {
            try
            {
                // Create test file in AppDataDirectory (guaranteed to work)
                var testFilePath = Path.Combine(FileSystem.AppDataDirectory, "test_database_location.txt");
                
                // Force database creation by adding a test record
                System.Diagnostics.Debug.WriteLine("Creating test attendance record...");
                var testRecordSaved = await SaveOfflineAttendanceAsync("TEST_STUDENT_001", "TimeIn", "TEST_DEVICE");
                System.Diagnostics.Debug.WriteLine($"Test record saved: {testRecordSaved}");

                // Create a simple text file that's easy to find
                var simpleTestFile = Path.Combine(FileSystem.AppDataDirectory, "SIMPLE_TEST.txt");
                await File.WriteAllTextAsync(simpleTestFile, $"Test file created at {DateTime.Now}");

                // Try multiple external storage locations
                var externalFileCreated = false;
                var externalPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Download"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures"),
                    "/storage/emulated/0/Download",
                    "/storage/emulated/0/Downloads",
                    "/storage/emulated/0/Documents"
                };

                foreach (var externalPath in externalPaths)
                {
                    try
                    {
                        if (Directory.Exists(externalPath))
                        {
                            var externalTestFile = Path.Combine(externalPath, "ATTRAK_TEST.txt");
                            await File.WriteAllTextAsync(externalTestFile, $"Attrak test file created at {DateTime.Now}\nDatabase: {_databasePath}\nExternal Path: {externalPath}");
                            externalFileCreated = true;
                            System.Diagnostics.Debug.WriteLine($"External file created successfully at: {externalTestFile}");
                            break; // Stop after first successful creation
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not create file in {externalPath}: {ex.Message}");
                    }
                }

                if (!externalFileCreated)
                {
                    System.Diagnostics.Debug.WriteLine("Could not create external file in any location - permissions may be restricted");
                }

                // Create the detailed test file with all information
                var testContent = $"Database Location Test\n" +
                                 $"Created: {DateTime.Now}\n" +
                                 $"Database Path: {_databasePath}\n" +
                                 $"App Data Path: {FileSystem.AppDataDirectory}\n" +
                                 $"Database Exists: {File.Exists(_databasePath)}\n" +
                                 $"Test File Path: {testFilePath}\n" +
                                 $"Test Record Saved: {testRecordSaved}\n" +
                                 $"External File Created: {externalFileCreated}\n" +
                                 $"Storage Permission: Check Android Settings > Apps > ScannerMaui > Permissions > Storage\n" +
                                 $"If no external file, try enabling 'All files access' permission";

                await File.WriteAllTextAsync(testFilePath, testContent);

                System.Diagnostics.Debug.WriteLine($"Test file created at: {testFilePath}");
                System.Diagnostics.Debug.WriteLine($"Simple test file created at: {simpleTestFile}");
                System.Diagnostics.Debug.WriteLine($"Database file exists: {File.Exists(_databasePath)}");
                System.Diagnostics.Debug.WriteLine($"Database path: {_databasePath}");
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating test database file: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        public async Task CheckSyncStatusAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== Checking Sync Status ===");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Check all records in offline_daily_attendance
                var allRecordsCommand = new SqliteCommand(
                    "SELECT attendance_id, student_id, is_synced FROM offline_daily_attendance ORDER BY created_at DESC",
                    connection);
                
                using var reader = await allRecordsCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var attendanceId = reader.GetString("attendance_id");
                    var studentId = reader.GetString("student_id");
                    var isSynced = reader.GetInt32("is_synced");
                    
                    System.Diagnostics.Debug.WriteLine($"Record: {attendanceId}, Student: {studentId}, Synced: {isSynced}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking sync status: {ex.Message}");
            }
        }

        public async Task<bool> ClearSyncedRecordsAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== Clearing Synced Records ===");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // First, count how many synced records exist
                var countCommand = new SqliteCommand(
                    "SELECT COUNT(*) FROM offline_daily_attendance WHERE is_synced = 1",
                    connection);
                var syncedCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
                System.Diagnostics.Debug.WriteLine($"Found {syncedCount} synced records to delete");

                // Delete all synced records from offline_daily_attendance in one operation
                var deleteCommand = new SqliteCommand(
                    "DELETE FROM offline_daily_attendance WHERE is_synced = 1",
                    connection);
                
                var result = await deleteCommand.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine($"Successfully deleted {result} synced records from offline_daily_attendance");
                
                // Also delete from offline_attendance table
                var countCommand2 = new SqliteCommand(
                    "SELECT COUNT(*) FROM offline_attendance WHERE is_synced = 1",
                    connection);
                var syncedCount2 = Convert.ToInt32(await countCommand2.ExecuteScalarAsync());
                System.Diagnostics.Debug.WriteLine($"Found {syncedCount2} synced records in offline_attendance to delete");

                var deleteCommand2 = new SqliteCommand(
                    "DELETE FROM offline_attendance WHERE is_synced = 1",
                    connection);
                
                var result2 = await deleteCommand2.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine($"Successfully deleted {result2} synced records from offline_attendance");
                
                var totalDeleted = result + result2;
                System.Diagnostics.Debug.WriteLine($"Total synced records deleted: {totalDeleted}");
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing synced records: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        public async Task<bool> MarkAsSyncedByStudentIdAsync(string studentId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var command = new SqliteCommand(
                    "UPDATE offline_daily_attendance SET is_synced = 1 WHERE student_id = @studentId",
                    connection);
                command.Parameters.AddWithValue("@studentId", studentId);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error marking student synced: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ForceMarkAllAsSyncedAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var command = new SqliteCommand(
                    "UPDATE offline_daily_attendance SET is_synced = 1",
                    connection);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error force marking all synced: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ClearAllOfflineDataAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var command1 = new SqliteCommand("DELETE FROM offline_daily_attendance", connection);
                await command1.ExecuteNonQueryAsync();

                var command2 = new SqliteCommand("DELETE FROM offline_attendance", connection);
                await command2.ExecuteNonQueryAsync();

                // Also clear the student names cache to ensure a true "Student 1" reset
                var command3 = new SqliteCommand("DELETE FROM student_names_cache", connection);
                await command3.ExecuteNonQueryAsync();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing offline data: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> TestDatabaseConnectionAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Database test failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CleanupDuplicateRecords()
        {
            return await ClearSyncedRecordsAsync();
        }

        public async Task<bool> ForceClearAllAttendanceData()
        {
            return await ClearAllOfflineDataAsync();
        }
    }

    public class OfflineAttendanceRecord
    {
        public string Id { get; set; } = string.Empty; // UUID string matching attendance_id in DB
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty; // Resolved from student_names_cache
        public string AttendanceType { get; set; } = string.Empty;
        public DateTime ScanTime { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public bool IsSynced { get; set; }
        public int SyncStatus { get; set; } // 0=Pending, 1=Synced, 2=Rejected
        public string Remarks { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string TeacherId { get; set; } = string.Empty;
    }


    public class PendingStudent
    {
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string AttendanceType { get; set; } = string.Empty;
        public DateTime ScanTime { get; set; }
        public string? DeviceId { get; set; }
        public int RecordCount { get; set; }
        public string? RecordId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class SyncRecordDetail
    {
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty; // TimeIn/TimeOut
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class SyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int SuccessCount { get; set; }
        public int FailCount { get; set; }
        public List<string> InvalidStudents { get; set; } = new List<string>();
        public List<SyncRecordDetail> Details { get; set; } = new List<SyncRecordDetail>();
    }

    public class OfflineDailyAttendanceRecord
    {
        public string AttendanceId { get; set; } = string.Empty;
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string? TimeIn { get; set; }
        public string? TimeOut { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? DeviceId { get; set; }
        public bool IsSynced { get; set; }
        public int SyncStatus { get; set; }
        public string AttendanceType { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
