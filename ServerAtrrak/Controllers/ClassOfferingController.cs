using Microsoft.AspNetCore.Mvc;
using AttrackSharedClass.Models;
using ServerAtrrak.Services;

namespace ServerAtrrak.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ClassOfferingController : ControllerBase
    {
        private readonly ClassOfferingService _service;
        private readonly ILogger<ClassOfferingController> _logger;

        public ClassOfferingController(ClassOfferingService service, ILogger<ClassOfferingController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet("all")]
        public async Task<ActionResult<List<ClassOffering>>> GetAll([FromQuery] string? schoolId)
        {
            var list = await _service.GetAllAsync(schoolId);
            return Ok(list);
        }

        [HttpGet("adviser/{adviserId}")]
        public async Task<ActionResult<List<ClassOffering>>> GetByAdviser(string adviserId)
        {
            var list = await _service.GetByAdviserAsync(adviserId);
            return Ok(list);
        }

        [HttpGet("section")]
        public async Task<ActionResult<List<ClassOffering>>> GetBySection(
            [FromQuery] string adviserId,
            [FromQuery] string section,
            [FromQuery] int gradeLevel,
            [FromQuery] string? dayOfWeek = null)
        {
            if (string.IsNullOrEmpty(adviserId) || string.IsNullOrEmpty(section)) return BadRequest();
            var list = await _service.GetBySectionAsync(adviserId, section, gradeLevel, dayOfWeek);
            return Ok(list);
        }

        [HttpGet("available")]
        public async Task<ActionResult<List<ClassOffering>>> GetAvailable(
            [FromQuery] string? schoolId,
            [FromQuery] int? gradeLevel,
            [FromQuery] string? strand)
        {
            var list = await _service.GetAvailableForTeacherAsync(schoolId, gradeLevel, strand);
            return Ok(list);
        }

        [HttpGet("teacher/{teacherId}")]
        public async Task<ActionResult<List<ClassOffering>>> GetByTeacher(string teacherId)
        {
            var list = await _service.GetByTeacherAsync(teacherId);
            return Ok(list);
        }

        [HttpGet("{classOfferingId}")]
        public async Task<ActionResult<ClassOffering>> GetById(string classOfferingId)
        {
            var item = await _service.GetByIdAsync(classOfferingId);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpGet("subjects-for-grade")]
        public async Task<ActionResult<List<TeacherSubjectAssignment>>> GetSubjectsForGrade(
            [FromQuery] int gradeLevel,
            [FromQuery] string? strand)
        {
            var list = await _service.GetSubjectsForGradeStrandAsync(gradeLevel, strand);
            return Ok(list);
        }

        [HttpGet("search-subjects")]
        public async Task<ActionResult<List<TeacherSubjectAssignment>>> SearchSubjects(
            [FromQuery] int gradeLevel,
            [FromQuery] string? strand,
            [FromQuery] string? keyword)
        {
            var list = await _service.SearchSubjectsAsync(gradeLevel, strand, keyword);
            return Ok(list);
        }

        [HttpPost]
        public async Task<ActionResult<ClassOfferingResponse>> Create([FromBody] CreateClassOfferingRequest request)
        {
            if (request == null) return BadRequest();
            var result = await _service.CreateAsync(request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPut("{classOfferingId}")]
        public async Task<ActionResult<ClassOfferingResponse>> Update(
            string classOfferingId,
            [FromQuery] string adviserId,
            [FromBody] UpdateClassOfferingRequest request)
        {
            if (string.IsNullOrEmpty(adviserId)) return BadRequest("adviserId required");
            var result = await _service.UpdateAsync(classOfferingId, adviserId, request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPut("{classOfferingId}/assign")]
        public async Task<ActionResult<ClassOfferingResponse>> AssignTeacher(string classOfferingId, [FromBody] AssignTeacherRequest body)
        {
            if (string.IsNullOrEmpty(body?.TeacherId)) return BadRequest();
            var result = await _service.AssignTeacherAsync(classOfferingId, body.TeacherId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPut("{classOfferingId}/unassign")]
        public async Task<ActionResult<ClassOfferingResponse>> UnassignTeacher(string classOfferingId, [FromBody] UnassignRequest body)
        {
            if (string.IsNullOrEmpty(body?.AdviserId)) return BadRequest();
            var result = await _service.UnassignTeacherAsync(classOfferingId, body.AdviserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpDelete("{classOfferingId}")]
        public async Task<ActionResult<ClassOfferingResponse>> Delete(string classOfferingId, [FromQuery] string adviserId)
        {
            if (string.IsNullOrEmpty(adviserId)) return BadRequest();
            var result = await _service.DeleteAsync(classOfferingId, adviserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
    }

    public class AssignTeacherRequest { public string TeacherId { get; set; } = ""; }
    public class UnassignRequest { public string AdviserId { get; set; } = ""; }
}
