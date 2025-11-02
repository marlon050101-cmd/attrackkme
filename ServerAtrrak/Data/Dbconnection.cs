using MySql.Data.MySqlClient;

namespace ServerAtrrak.Data
{
    public class Dbconnection
    {
        public IConfiguration Configuration { get; }
        private readonly string _connectionString;
        
        public Dbconnection(IConfiguration configuration)
        {
            Configuration = configuration;
            _connectionString = Configuration.GetSection("ConnectionStrings").GetSection("dbconstring").Value ?? string.Empty;
        }
        
        public string GetConnection() => _connectionString;
        
        public async Task<MySqlConnection> GetConnectionAsync()
        {
            var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            return connection;
        }
    }
}
