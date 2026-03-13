using Microsoft.AspNetCore.Mvc;
using ServerAtrrak.Services;
using AttrackSharedClass.Models;

namespace ServerAtrrak.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AcademicPeriodController : ControllerBase
    {
        private readonly IAcademicPeriodService _periodService;
        private readonly ILogger<AcademicPeriodController> _logger;

        public AcademicPeriodController(IAcademicPeriodService periodService, ILogger<AcademicPeriodController> logger)
        {
            _periodService = periodService;
            _logger = logger;
        }

        [HttpGet("active/{schoolId}")]
        public async Task<ActionResult<AcademicPeriod>> GetActivePeriod(string schoolId)
        {
            var period = await _periodService.GetActivePeriodAsync(schoolId);
            if (period == null) return NotFound("No active academic period found");
            return Ok(period);
        }

        [HttpGet("all/{schoolId}")]
        public async Task<ActionResult<List<AcademicPeriod>>> GetAllPeriods(string schoolId)
        {
            var periods = await _periodService.GetAllPeriodsAsync(schoolId);
            return Ok(periods);
        }

        [HttpPost("activate/{schoolId}/{periodId}/{academicLevel}")]
        public async Task<ActionResult> SetActivePeriod(string schoolId, string periodId, string academicLevel)
        {
            var success = await _periodService.SetActivePeriodAsync(schoolId, periodId, academicLevel);
            if (success) return Ok();
            return BadRequest("Failed to set active period");
        }

        [HttpPost("create")]
        public async Task<ActionResult> CreatePeriod([FromBody] CreatePeriodRequest request)
        {
            if (request == null) return BadRequest("Invalid request");
            
            var success = await _periodService.CreatePeriodAsync(request);
            if (success) return Ok();
            return BadRequest("Failed to create academic period");
        }
    }
}
