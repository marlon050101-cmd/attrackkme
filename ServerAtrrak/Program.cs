using ServerAtrrak.Data;
using ServerAtrrak.Services;

namespace ServerAtrrak
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "ServerAtrrak API",
                    Version = "v1",
                    Description = "Attendance Management System API"
                });
            });
            builder.Services.AddScoped<Dbconnection>();
            
            // Register authentication services
            builder.Services.AddScoped<IAuthService, AuthService>();
            
            // Register school services
            builder.Services.AddScoped<SchoolService>();
            
            // Register teacher subject services
            builder.Services.AddScoped<TeacherSubjectService>();
            
            // Register attendance services
            builder.Services.AddScoped<AttendanceService>();
            
            // Register QR validation services
            builder.Services.AddScoped<QRValidationService>();
            
            // Register guidance services
            builder.Services.AddScoped<IGuidanceService, GuidanceServiceNoDateFilter>();
            
            // Register teacher services
            builder.Services.AddScoped<TeacherService>();
  
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", builder =>
                {
                    builder
                    .SetIsOriginAllowed(_ => true)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
                });
            });

            // Add health checks
            builder.Services.AddHealthChecks();
            
            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseCors("AllowAll");
            app.UseAuthorization();

            // Add health check endpoint
            app.MapHealthChecks("/api/health");

            app.MapControllers();

            app.Run();
        }
    }
}
