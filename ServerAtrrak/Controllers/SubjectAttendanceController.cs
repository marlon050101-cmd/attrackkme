using Microsoft.AspNetCore.Mvc;
using AttrackSharedClass.Models;
using ServerAtrrak.Services;

namespace ServerAtrrak.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubjectAttendanceController : ControllerBase
    {
        private readonly SubjectAttendanceService _service;
        private readonly ILogger<SubjectAttendanceController> _logger;

        public SubjectAttendanceController(SubjectAttendanceService service, ILogger<SubjectAttendanceController> logger)
        {
            _service = service;
            _logger = logger;
        }


        [HttpPost("batch")]
        public async Task<ActionResult<SubjectAttendanceResponse>> SaveBatch([FromBody] SubjectAttendanceBatchRequest request)
        {
            if (request == null)
                return BadRequest(new SubjectAttendanceResponse { Success = false, Message = "Invalid request." });
            var result = await _service.SaveBatchAsync(request);
            if (result.Success) return Ok(result);
            return BadRequest(result);
        }


        [HttpGet("class-offering/{classOfferingId}/roster")]
        public async Task<ActionResult<List<StudentDisplayInfo>>> GetClassOfferingRoster(string classOfferingId)
        {
            try
            {
                var list = await _service.GetClassRosterByOfferingAsync(classOfferingId);
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting class offering roster");
                return StatusCode(500, new List<StudentDisplayInfo>());
            }
        }

        [HttpGet("class-offering/{classOfferingId}/date/{date}")]
        public async Task<ActionResult<List<SubjectAttendanceRecord>>> GetByClassOfferingAndDate(string classOfferingId, string date, [FromQuery] string? adviserId = null)
        {
            try
            {
                if (!DateTime.TryParse(date, out var parsedDate))
                    return BadRequest("Invalid date format. Use YYYY-MM-DD");

                var list = await _service.GetByClassOfferingAndDateAsync(classOfferingId, parsedDate, adviserId);
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting subject attendance by class offering");
                return StatusCode(500, new List<SubjectAttendanceRecord>());
            }
        }

        [HttpGet("student/{studentId}/history")]
        public async Task<ActionResult<List<SubjectAttendanceRecord>>> GetStudentHistory(string studentId, [FromQuery] int days = 30)
        {
            try
            {
                var list = await _service.GetStudentHistoryAsync(studentId, days);
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting student subject attendance history");
                return StatusCode(500, new List<SubjectAttendanceRecord>());
            }
        }

        [HttpGet("teacher/{teacherId}/history")]
        public async Task<ActionResult<List<SubjectAttendanceRecord>>> GetByTeacher(string teacherId, [FromQuery] int days = 30)
        {
            try
            {
                var list = await _service.GetTeacherHistoryAsync(teacherId, days);
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting teacher subject attendance history");
                return StatusCode(500, new List<SubjectAttendanceRecord>());
            }
        }
        [HttpGet("adviser/{adviserId}/subjects")]
        public async Task<ActionResult<List<ClassOffering>>> GetAdviserSubjects(string adviserId)
        {
            try
            {
                var list = await _service.GetAdviserSubjectsAsync(adviserId);
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting adviser subjects");
                return StatusCode(500, new List<ClassOffering>());
            }
        }
        [HttpGet("adviser/{adviserId}/daily-summary/{date:datetime}")]
        public async Task<ActionResult<List<DailySubjectSummary>>> GetDailyAdviserSummary(string adviserId, DateTime date)
        {
            try
            {
                var list = await _service.GetDailySubjectSummaryAsync(adviserId, date);
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting daily subject summary for adviser");
                return StatusCode(500, new List<DailySubjectSummary>());
            }
        }

        [HttpPut("daily-summary/remarks")]
        public async Task<ActionResult> UpdateDailySummaryRemarks([FromQuery] string studentId, [FromQuery] DateTime date, [FromBody] string remarks)
        {
            try
            {
                var success = await _service.UpdateDailySummaryRemarksAsync(studentId, date, remarks);
                if (success) return Ok(new { Message = "Remarks updated." });
                return BadRequest(new { Message = "Failed to update remarks." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating daily summary remarks");
                return StatusCode(500, new { Message = "Internal server error." });
            }
        }
        [HttpPut("daily-summary/status")]
        public async Task<ActionResult> UpdateDailySummaryStatus([FromQuery] string studentId, [FromQuery] DateTime date, [FromBody] string status)
        {
            try
            {
                var success = await _service.UpdateDailySummaryStatusAsync(studentId, date, status);
                if (success) return Ok(new { Message = "Status updated." });
                return BadRequest(new { Message = "Failed to update status." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating daily summary status");
                return StatusCode(500, new { Message = "Internal server error." });
            }
        }
    }
}
