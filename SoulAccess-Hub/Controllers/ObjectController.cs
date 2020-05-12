using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SoulAccess.Hub.Controllers {
    [Route("api/v1/[controller]")]
    [ApiController]
    public class ObjectController : ControllerBase {
        const int MAX_NENTRY = 20;
        private readonly ObjectIndexer _Idxr;

        public ObjectController(ObjectIndexer idxr) {
            _Idxr = idxr;
        }

        public class GetRangeParams {
            public DateTime? Since { get; set; }
            public DateTime? Until { get; set; }
        }

        // GET: api/v1/object
        [HttpGet]
        public IEnumerable<ObjectIndex> Get([FromQuery] GetRangeParams rng) {
            var ie = _Idxr as IEnumerable<ObjectIndex>;
            if (rng.Since.HasValue) {
                var since = rng.Since.Value.ToUniversalTime();
                ie = ie.SkipWhile(x => x.LastModifiedUtc < since);
            }
            if (rng.Until.HasValue) {
                var until = rng.Until.Value.ToUniversalTime();
                ie = ie.TakeWhile(x => x.LastModifiedUtc <= until);
            }
            return ie.Take(MAX_NENTRY);
        }

        // GET: api/v1/object/5.zip/meta
        [HttpGet("{name}/meta")]
        public ActionResult GetMeta(string name) {
            if (_Idxr.TryGetIndex(name, out var idx)) {
                return Ok(idx);
            } else {
                return NotFound("object not found");
            }
        }

        // Get: api/v1/object/5.zip
        [HttpGet("{name}")]
        public async Task<ActionResult> Get(string name) {
            var e = await _Idxr.ReadAsync(name, Response.Body);
            if (e == null) {
                Response.ContentType = "application/octet-stream";
                return Ok();
            } else {
                return BadRequest(e);
            }
        }

        // POST: api/v1/object/5.zip
        [HttpPost("{name}")]
        public async Task<ActionResult> Post(string name, long from) {
            var e = await _Idxr.WriteAsync(name, from, Request.Body);
            if (e == null) {
                e = await _Idxr.UpdateIndexAsync(name);
            }
            if (e == null && _Idxr.TryGetIndex(name, out var idx)) {
                return Ok(idx);
            } else {
                return BadRequest(e);
            }
        }

        // DELETE: api/v1/object/5.zip
        [HttpDelete("{name}")]
        public async Task<ActionResult> Delete(string name) {
            var e = await _Idxr.RemoveAsync(name);
            if (e == null) {
                return Ok();
            } else {
                return BadRequest(e);
            }
        }
    }
}
