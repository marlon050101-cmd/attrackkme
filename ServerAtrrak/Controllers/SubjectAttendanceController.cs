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

        [HttpGet("class-offering/{classOfferingId}/date/{date:datetime}")]
        public async Task<ActionResult<List<SubjectAttendanceRecord>>> GetByClassOfferingAndDate(string classOfferingId, DateTime date)
        {
            try
            {
                var list = await _service.GetByClassOfferingAndDateAsync(classOfferingId, date.Date);
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
    }
}
