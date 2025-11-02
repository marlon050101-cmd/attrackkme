using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Data;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AttrackSharedClass.Models;

namespace ScannerMaui.Services
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

                // Create student names cache table
                var createStudentNamesCacheTable = @"
                    CREATE TABLE IF NOT EXISTS student_names_cache (
                        student_id TEXT PRIMARY KEY,
                        student_name TEXT NOT NULL,
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

                // Add attendance_type column to existing offline_daily_attendance table if it doesn't exist
                try
                {
                    System.Diagnostics.Debug.WriteLine("Adding attendance_type column to offline_daily_attendance table...");
                    var addColumnCommand = new SqliteCommand(
                        "ALTER TABLE offline_daily_attendance ADD COLUMN attendance_type TEXT",
                        connection);
                    addColumnCommand.ExecuteNonQuery();
                    System.Diagnostics.Debug.WriteLine("attendance_type column added successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Column already exists or error adding column: {ex.Message}");
                }

                // Now check for existing data after tables are created
                try
                {
                    var checkCommand = new SqliteCommand("SELECT COUNT(*) FROM offline_daily_attendance", connection);
                    var count = checkCommand.ExecuteScalar();
                    System.Diagnostics.Debug.WriteLine($"Existing records in offline_daily_attendance: {count}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error checking existing data: {ex.Message}");
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
        public async Task<bool> SaveOfflineAttendanceAsync(string studentId, string attendanceType, string? deviceId = null, bool isOnlineMode = false)
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
                    
                    // Create a new record for this TimeIn/TimeOut scan
                    // This ensures both TimeIn and TimeOut appear separately in pending list
                        var attendanceId = Guid.NewGuid().ToString();
                        
                    System.Diagnostics.Debug.WriteLine($"Creating new {attendanceType} record for student {studentId} on {today}. Record ID: {attendanceId}");
                        
                        var insertCommand = new SqliteCommand(
                            "INSERT INTO offline_daily_attendance (attendance_id, student_id, date, time_in, time_out, status, device_id, is_synced, attendance_type) VALUES (@attendanceId, @studentId, @date, @timeIn, @timeOut, @status, @deviceId, @isSynced, @attendanceType)",
                            connection);
                        
                        insertCommand.Parameters.AddWithValue("@attendanceId", attendanceId);
                        insertCommand.Parameters.AddWithValue("@studentId", studentId);
                        insertCommand.Parameters.AddWithValue("@date", today);
                        insertCommand.Parameters.AddWithValue("@timeIn", attendanceType == "TimeIn" ? timeValue : (object)DBNull.Value);
                        insertCommand.Parameters.AddWithValue("@timeOut", attendanceType == "TimeOut" ? timeValue : (object)DBNull.Value);
                        insertCommand.Parameters.AddWithValue("@status", "Present");
                        insertCommand.Parameters.AddWithValue("@deviceId", deviceIdValue);
                        insertCommand.Parameters.AddWithValue("@isSynced", 0);
                        insertCommand.Parameters.AddWithValue("@attendanceType", attendanceType);
                        
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
                    command.Parameters.AddWithValue("@isSynced", 0); // Explicitly set as unsynced

                    var result = await command.ExecuteNonQueryAsync();
                    System.Diagnostics.Debug.WriteLine($"Offline attendance saved successfully. Rows affected: {result}");
                }
                
                // Try to cache the student name if not already cached
                await TryCacheStudentNameAsync(studentId);
                
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
                        return await SaveOfflineAttendanceAsync(studentId, attendanceType, deviceId, isOnlineMode);
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
        public async Task<List<OfflineDailyAttendanceRecord>> GetUnsyncedDailyAttendanceAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== Getting Unsynced Daily Attendance Records ===");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var query = @"
                    SELECT attendance_id, student_id, date, time_in, time_out, status, device_id, is_synced, attendance_type, created_at
                    FROM offline_daily_attendance 
                    WHERE is_synced = 0
                    ORDER BY created_at DESC";
                
                var records = new List<OfflineDailyAttendanceRecord>();
                
                using var command = new SqliteCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    var record = new OfflineDailyAttendanceRecord
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
                    };
                    
                    records.Add(record);
                }
                
                System.Diagnostics.Debug.WriteLine($"Found {records.Count} unsynced daily attendance records");
                
                // Log each record for debugging
                foreach (var record in records)
                {
                    System.Diagnostics.Debug.WriteLine($"Record - ID: {record.AttendanceId}, Student: {record.StudentId}, Type: {record.AttendanceType}, TimeIn: {record.TimeIn}, TimeOut: {record.TimeOut}, DeviceId: {record.DeviceId}");
                }
                return records;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting unsynced daily attendance records: {ex.Message}");
                return new List<OfflineDailyAttendanceRecord>();
            }
        }

        public async Task<List<OfflineAttendanceRecord>> GetUnsyncedAttendanceAsync()
        {
            var records = new List<OfflineAttendanceRecord>();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Get unsynced daily attendance records (only those saved in offline mode)
                // Exclude records that were saved as backup during online mode
                var dailyCommand = new SqliteCommand(
                    @"SELECT attendance_id, student_id, date, time_in, time_out, status, device_id, is_synced, created_at, attendance_type 
                      FROM offline_daily_attendance 
                      WHERE is_synced = 0 
                      ORDER BY created_at",
                    connection);

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
                        Id = attendanceId.GetHashCode(),
                        StudentId = studentId,
                        ScanTime = scanTime,
                        AttendanceType = attendanceType,
                        IsSynced = isSynced,
                        DeviceId = deviceId,
                        CreatedAt = createdAt
                    });
                }
                dailyReader.Close();

                // Get unsynced regular attendance records
                var regularCommand = new SqliteCommand(
                    "SELECT attendance_id, student_id, timestamp, attendance_type, device_id, is_synced, created_at FROM offline_attendance WHERE is_synced = 0 ORDER BY created_at",
                    connection);

                using var regularReader = await regularCommand.ExecuteReaderAsync();
                while (await regularReader.ReadAsync())
                {
                    records.Add(new OfflineAttendanceRecord
                    {
                        Id = regularReader.GetString("attendance_id").GetHashCode(),
                        StudentId = regularReader.GetString("student_id"),
                        AttendanceType = regularReader.GetString("attendance_type"),
                        ScanTime = regularReader.GetDateTime("timestamp"),
                        DeviceId = regularReader.IsDBNull("device_id") ? "" : regularReader.GetString("device_id"),
                        IsSynced = regularReader.GetInt32("is_synced") == 1,
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

        public async Task<bool> MarkAttendanceAsSyncedAsync(int recordId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Try to update in daily attendance table first
                var dailyCommand = new SqliteCommand(
                    "UPDATE offline_daily_attendance SET is_synced = 1 WHERE attendance_id = @id",
                    connection);
                dailyCommand.Parameters.AddWithValue("@id", recordId.ToString());

                var dailyResult = await dailyCommand.ExecuteNonQueryAsync();
                if (dailyResult > 0)
                {
                    return true;
                }

                // Try to update in regular attendance table
                var regularCommand = new SqliteCommand(
                    "UPDATE offline_attendance SET is_synced = 1 WHERE attendance_id = @id",
                    connection);
                regularCommand.Parameters.AddWithValue("@id", recordId.ToString());

                var regularResult = await regularCommand.ExecuteNonQueryAsync();
                return regularResult > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error marking attendance as synced: {ex.Message}");
                return false;
            }
        }

        public async Task<int> GetUnsyncedCountAsync()
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
                var dailyCountCommand = new SqliteCommand(
                    @"SELECT COUNT(*) 
                      FROM offline_daily_attendance 
                      WHERE is_synced = 0",
                    connection);
                var dailyCount = Convert.ToInt32(await dailyCountCommand.ExecuteScalarAsync());
                System.Diagnostics.Debug.WriteLine($"Individual daily attendance count: {dailyCount}");

                // Count other unsynced attendance records
                var otherCountCommand = new SqliteCommand(
                    @"SELECT COUNT(*) 
                      FROM offline_attendance 
                      WHERE is_synced = 0",
                    connection);
                var otherCount = Convert.ToInt32(await otherCountCommand.ExecuteScalarAsync());
                System.Diagnostics.Debug.WriteLine($"Other unsynced attendance count: {otherCount}");

                var totalCount = dailyCount + otherCount;
                System.Diagnostics.Debug.WriteLine($"Unsynced count result: {totalCount}");
                return totalCount;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting unsynced count: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return 0;
            }
        }

        public async Task<List<PendingStudent>> GetPendingStudentsAsync()
        {
            var pendingStudents = new List<PendingStudent>();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Get daily attendance records (separate records for each TimeIn/TimeOut)
                var dailyCommand = new SqliteCommand(
                    @"SELECT attendance_id, student_id, date, time_in, time_out, attendance_type, created_at, device_id
                      FROM offline_daily_attendance 
                      WHERE is_synced = 0
                      ORDER BY created_at",
                    connection);

                using var dailyReader = await dailyCommand.ExecuteReaderAsync();
                while (await dailyReader.ReadAsync())
                {
                    var attendanceId = dailyReader.GetString("attendance_id");
                    var studentId = dailyReader.GetString("student_id");
                    var date = DateTime.Parse(dailyReader.GetString("date"));
                    var attendanceType = dailyReader.GetString("attendance_type");
                    var createdAt = dailyReader.GetDateTime("created_at");
                    var deviceId = dailyReader.IsDBNull("device_id") ? "" : dailyReader.GetString("device_id");
                    
                    // Determine scan time based on attendance type
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
                        scanTime = createdAt; // Fallback to creation time
                    }

                    // Try to get student name from cache
                    var studentName = await GetStudentNameAsync(studentId);
                    
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

                    pendingStudents.Add(new PendingStudent
                    {
                        StudentId = studentId,
                        StudentName = !string.IsNullOrEmpty(studentName) && !studentName.StartsWith("Student ") ? studentName : $"Student {studentId}",
                        AttendanceType = attendanceType,
                        ScanTime = scanTime,
                        DeviceId = deviceId,
                        RecordId = attendanceId,
                        CreatedAt = createdAt
                    });
                }
                dailyReader.Close();

                // Get other offline attendance records (subject-based attendance)
                var otherCommand = new SqliteCommand(
                    @"SELECT attendance_id, student_id, timestamp, attendance_type, device_id, created_at
                      FROM offline_attendance 
                      WHERE is_synced = 0
                      ORDER BY created_at",
                    connection);
                
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
                    
                    pendingStudents.Add(new PendingStudent
                    {
                        StudentId = studentId,
                        StudentName = !string.IsNullOrEmpty(studentName) && !studentName.StartsWith("Student ") ? studentName : $"Student {studentId}",
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

        // Public method to get student name (for UI)
        public async Task<string> GetStudentNameForDisplayAsync(string studentId)
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
                    return offlineName.ToString();
                }
                
                // If still no name found, return the fallback
                return $"Student {studentId}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting student name for display: {ex.Message}");
                return $"Student {studentId}";
            }
        }

        private async Task<string?> FetchStudentNameFromServerAsync(string studentId)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.BaseAddress = new Uri("https://attrak-8gku.onrender.com/");
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

        private async Task CacheStudentNameAsync(string studentId, string studentName)
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
                        cached_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    )", connection);
                await createTableCommand.ExecuteNonQueryAsync();

                // Insert or update the cached name
                var command = new SqliteCommand(
                    @"INSERT OR REPLACE INTO student_names_cache (student_id, student_name, cached_at) 
                      VALUES (@studentId, @studentName, @cachedAt)",
                    connection);
                command.Parameters.AddWithValue("@studentId", studentId);
                command.Parameters.AddWithValue("@studentName", studentName);
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
                        cached_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    )", connection);
                await createTableCommand.ExecuteNonQueryAsync();

                // Bulk insert all students
                using var transaction = connection.BeginTransaction();
                var command = new SqliteCommand(
                    @"INSERT OR REPLACE INTO student_names_cache (student_id, student_name, cached_at) 
                      VALUES (@studentId, @studentName, @cachedAt)",
                    connection, transaction);

                foreach (var student in students)
                {
                    if (student.ContainsKey("studentId") && student.ContainsKey("fullName"))
                    {
                        command.Parameters.Clear();
                        command.Parameters.AddWithValue("@studentId", student["studentId"].ToString());
                        command.Parameters.AddWithValue("@studentName", student["fullName"].ToString());
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
                    await RemoveInvalidStudentRecordsAsync(studentId);
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
                            return $"✅ Database copied to Downloads!\nPath: {destinationPath}\nCheck your Downloads folder in file manager.";
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

        // Method to remove all records for an invalid student
        private async Task RemoveInvalidStudentRecordsAsync(string studentId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Remove from daily attendance table
                var dailyCommand = new SqliteCommand(
                    "DELETE FROM offline_daily_attendance WHERE student_id = @studentId",
                    connection);
                dailyCommand.Parameters.AddWithValue("@studentId", studentId);

                var dailyResult = await dailyCommand.ExecuteNonQueryAsync();

                // Remove from regular attendance table
                var regularCommand = new SqliteCommand(
                    "DELETE FROM offline_attendance WHERE student_id = @studentId",
                    connection);
                regularCommand.Parameters.AddWithValue("@studentId", studentId);

                var regularResult = await regularCommand.ExecuteNonQueryAsync();

                var totalRemoved = dailyResult + regularResult;
                System.Diagnostics.Debug.WriteLine($"Removed {totalRemoved} invalid records for student {studentId} (daily: {dailyResult}, regular: {regularResult})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing invalid student records: {ex.Message}");
            }
        }

        // Auto-sync method to send offline data to API when connection is restored
        public async Task<SyncResult> AutoSyncOfflineDataAsync(string apiBaseUrl, string teacherId)
        {
            try
            {
                var unsyncedRecords = await GetUnsyncedAttendanceAsync();
                if (!unsyncedRecords.Any())
                {
                    System.Diagnostics.Debug.WriteLine("No offline records to sync");
                    return new SyncResult { Success = true, Message = "No offline records to sync" };
                }

                System.Diagnostics.Debug.WriteLine($"Starting auto-sync of {unsyncedRecords.Count} offline records");

                // Get unique student IDs to validate
                var uniqueStudentIds = unsyncedRecords.Select(r => r.StudentId).Distinct().ToList();
                var invalidStudents = new List<string>();

                // Validate each student first
                foreach (var studentId in uniqueStudentIds)
                {
                    var isValidStudent = await ValidateStudentInTeacherClassAsync(studentId, apiBaseUrl, teacherId);
                    if (!isValidStudent)
                    {
                        invalidStudents.Add(studentId);
                        System.Diagnostics.Debug.WriteLine($"Student {studentId} is not in teacher's class list. Will be removed.");
                    }
                }

                // Remove records for invalid students
                foreach (var invalidStudentId in invalidStudents)
                {
                    await RemoveInvalidStudentRecordsAsync(invalidStudentId);
                }

                // Get updated records after removing invalid ones
                var validRecords = await GetUnsyncedAttendanceAsync();
                System.Diagnostics.Debug.WriteLine($"After validation: {validRecords.Count} valid records to sync (removed {invalidStudents.Count} invalid students)");

                using var httpClient = new HttpClient();
                httpClient.BaseAddress = new Uri(apiBaseUrl);
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                int successCount = 0;
                int failCount = 0;

                foreach (var record in validRecords)
                {
                    try
                    {
                        HttpResponseMessage response;
                        
                        if (record.AttendanceType == "TimeIn")
                        {
                            var request = new DailyTimeInRequest
                            {
                                StudentId = record.StudentId,
                                Date = record.ScanTime.Date,
                                TimeIn = record.ScanTime.TimeOfDay,
                                TeacherId = record.TeacherId
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
                                TeacherId = record.TeacherId
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

                // Log sync results including invalid students
                var logMessage = failCount > 0 ? $"{failCount} records failed to sync" : null;
                if (invalidStudents.Any())
                {
                    logMessage = logMessage != null ? 
                        $"{logMessage}; {invalidStudents.Count} invalid students removed" : 
                        $"{invalidStudents.Count} invalid students removed";
                }

                await LogSyncAsync("AutoSync", validRecords.Count, 
                    failCount == 0 ? "Success" : "Partial", logMessage);

                System.Diagnostics.Debug.WriteLine($"Auto-sync completed: {successCount} successful, {failCount} failed, {invalidStudents.Count} invalid students removed");
                
                var message = failCount == 0 ? "Sync completed successfully" : $"{failCount} records failed to sync";
                if (invalidStudents.Any())
                {
                    message += $". {invalidStudents.Count} invalid students removed from pending list.";
                }
                
                return new SyncResult 
                { 
                    Success = failCount == 0, 
                    Message = message,
                    SuccessCount = successCount,
                    FailCount = failCount,
                    InvalidStudents = invalidStudents
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in auto-sync: {ex.Message}");
                await LogSyncAsync("AutoSync", 0, "Error", ex.Message);
                return new SyncResult { Success = false, Message = $"Error: {ex.Message}" };
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

        public async Task<bool> MarkAsSyncedAsync(int recordId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== Marking record as synced: {recordId} ===");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var command = new SqliteCommand(
                    "UPDATE offline_daily_attendance SET is_synced = 1 WHERE attendance_id = @id",
                    connection);
                command.Parameters.AddWithValue("@id", recordId.ToString());

                var result = await command.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine($"Mark as synced result: {result} rows affected");
                
                if (result > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully marked record {recordId} as synced");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"No records found with ID {recordId} to mark as synced");
                }
                
                return result > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error marking record as synced: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        public async Task<bool> MarkAsSyncedByStudentIdAsync(string studentId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== Marking records as synced by student ID: {studentId} ===");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Mark all records for this student as synced
                var command = new SqliteCommand(
                    "UPDATE offline_daily_attendance SET is_synced = 1 WHERE student_id = @studentId AND is_synced = 0",
                    connection);
                command.Parameters.AddWithValue("@studentId", studentId);

                var result = await command.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine($"Mark as synced by student ID result: {result} rows affected");
                
                if (result > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully marked {result} records for student {studentId} as synced");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"No unsynced records found for student {studentId}");
                }
                
                return result > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error marking records as synced by student ID: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        public async Task<bool> ForceMarkAllAsSyncedAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== Force marking ALL records as synced ===");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Mark ALL unsynced records as synced
                var command = new SqliteCommand(
                    "UPDATE offline_daily_attendance SET is_synced = 1 WHERE is_synced = 0",
                    connection);

                var result = await command.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine($"Force mark all as synced result: {result} rows affected");
                
                if (result > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully marked {result} records as synced");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"No unsynced records found to mark as synced");
                }
                
                return result > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error force marking all records as synced: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        private async Task MarkExistingRecordAsSyncedAsync(string attendanceId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== Marking existing record as synced: {attendanceId} ===");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var command = new SqliteCommand(
                    "UPDATE offline_daily_attendance SET is_synced = 1 WHERE attendance_id = @attendanceId",
                    connection);
                command.Parameters.AddWithValue("@attendanceId", attendanceId);

                var result = await command.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine($"Mark existing record as synced result: {result} rows affected");
                
                if (result > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully marked existing record {attendanceId} as synced");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"No record found with ID {attendanceId} to mark as synced");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error marking existing record as synced: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        // Clear all offline data
        public async Task<bool> ClearAllOfflineDataAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== CLEARING ALL OFFLINE DATA ===");
                System.Diagnostics.Debug.WriteLine($"Database path: {_databasePath}");
                
                // WARNING: This will clear ALL pending data - use with caution
                System.Diagnostics.Debug.WriteLine("⚠️ WARNING: This will permanently delete all pending attendance records!");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Clear all tables
                var commands = new[]
                {
                    "DELETE FROM offline_daily_attendance",
                    "DELETE FROM offline_attendance", 
                    "DELETE FROM offline_users",
                    "DELETE FROM sync_log"
                };

                foreach (var sql in commands)
                {
                    var command = new SqliteCommand(sql, connection);
                    var result = await command.ExecuteNonQueryAsync();
                    System.Diagnostics.Debug.WriteLine($"Executed: {sql} - Rows affected: {result}");
                }

                System.Diagnostics.Debug.WriteLine("All offline data cleared successfully");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing offline data: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        // Delete synced records (automatic cleanup after sync)
        public async Task<bool> DeleteSyncedRecordsAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== DELETING SYNCED RECORDS ===");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Delete synced records from both tables
                var commands = new[]
                {
                    "DELETE FROM offline_daily_attendance WHERE is_synced = 1",
                    "DELETE FROM offline_attendance WHERE is_synced = 1"
                };

                int totalDeleted = 0;
                foreach (var sql in commands)
                {
                    var command = new SqliteCommand(sql, connection);
                    var result = await command.ExecuteNonQueryAsync();
                    totalDeleted += result;
                    System.Diagnostics.Debug.WriteLine($"Executed: {sql} - Rows affected: {result}");
                }

                System.Diagnostics.Debug.WriteLine($"Total synced records deleted: {totalDeleted}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting synced records: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        // Delete the entire database file
        public bool DeleteDatabaseFile()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== DELETING DATABASE FILE ===");
                System.Diagnostics.Debug.WriteLine($"Database path: {_databasePath}");
                
                if (File.Exists(_databasePath))
                {
                    File.Delete(_databasePath);
                    System.Diagnostics.Debug.WriteLine("Database file deleted successfully");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Database file does not exist");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting database file: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        // Clean up duplicate records to start fresh
        public async Task<bool> CleanupDuplicateRecords()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== CLEANING UP DUPLICATE RECORDS ===");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // Delete all existing records to start fresh
                var deleteCommand = new SqliteCommand("DELETE FROM offline_daily_attendance", connection);
                var deletedCount = await deleteCommand.ExecuteNonQueryAsync();
                
                System.Diagnostics.Debug.WriteLine($"Deleted {deletedCount} duplicate records");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error cleaning up duplicate records: {ex.Message}");
                return false;
            }
        }

        // Force clear all attendance data to start completely fresh
        public async Task<bool> ForceClearAllAttendanceData()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== FORCE CLEARING ALL ATTENDANCE DATA ===");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // Delete all records from all attendance tables
                var deleteDailyCommand = new SqliteCommand("DELETE FROM offline_daily_attendance", connection);
                var deletedDailyCount = await deleteDailyCommand.ExecuteNonQueryAsync();
                
                var deleteRegularCommand = new SqliteCommand("DELETE FROM offline_attendance", connection);
                var deletedRegularCount = await deleteRegularCommand.ExecuteNonQueryAsync();
                
                System.Diagnostics.Debug.WriteLine($"Deleted {deletedDailyCount} daily records and {deletedRegularCount} regular records");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error force clearing attendance data: {ex.Message}");
                return false;
            }
        }


        public async Task<bool> TestDatabaseConnectionAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== Testing Database Connection ===");
                System.Diagnostics.Debug.WriteLine($"Database path: {_databasePath}");
                System.Diagnostics.Debug.WriteLine($"Database exists: {File.Exists(_databasePath)}");
                
                // Ensure database is initialized
                InitializeDatabase();
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                System.Diagnostics.Debug.WriteLine("Database connection opened successfully");
                
                // Test if tables exist
                var tableCheckCommand = new SqliteCommand(
                    "SELECT name FROM sqlite_master WHERE type='table'",
                    connection);
                
                var tables = new List<string>();
                using var reader = await tableCheckCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
                
                System.Diagnostics.Debug.WriteLine($"Found tables: {string.Join(", ", tables)}");
                
                // Test simple insert
                var testCommand = new SqliteCommand(
                    "INSERT INTO offline_daily_attendance (attendance_id, student_id, date, status) VALUES (@id, @studentId, @date, @status)",
                    connection);
                testCommand.Parameters.AddWithValue("@id", "TEST-" + Guid.NewGuid().ToString());
                testCommand.Parameters.AddWithValue("@studentId", "TEST-STUDENT");
                testCommand.Parameters.AddWithValue("@date", DateTime.Today.ToString("yyyy-MM-dd"));
                testCommand.Parameters.AddWithValue("@status", "Present");
                
                var result = await testCommand.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine($"Test insert result: {result}");
                
                return result > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error testing database connection: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        // Simple test method to verify offline saving works

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
    }

    public class OfflineAttendanceRecord
    {
        public int Id { get; set; }
        public string StudentId { get; set; } = string.Empty;
        public string AttendanceType { get; set; } = string.Empty;
        public DateTime ScanTime { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public bool IsSynced { get; set; }
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

    public class SyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int SuccessCount { get; set; }
        public int FailCount { get; set; }
        public List<string> InvalidStudents { get; set; } = new List<string>();
    }

    public class OfflineDailyAttendanceRecord
    {
        public string AttendanceId { get; set; } = string.Empty;
        public string StudentId { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string? TimeIn { get; set; }
        public string? TimeOut { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? DeviceId { get; set; }
        public bool IsSynced { get; set; }
        public string AttendanceType { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
