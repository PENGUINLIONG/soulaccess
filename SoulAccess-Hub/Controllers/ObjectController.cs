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

        // GET: api/v1/object/5.zip
        [HttpGet("{id}", Name = "Get")]
        public async Task<ActionResult> Get(string name) {
            try {
                if (!_Idxr.OpenRead(name, out var fs)) {
                    return BadRequest("object doesn't exists");
                }
                Response.ContentType = "application/octet-stream";
                using (fs) { await fs.CopyToAsync(Response.Body); }
                return Ok();
            } catch (Exception) {
                return BadRequest("transmission failed");
            }
        }

        // PUT: api/v1/object/5.zip
        [HttpPut("{id}")]
        public async Task<ActionResult> Put(string name) {
            try {
                if (!_Idxr.OpenWrite(name, out var fs)) {
                    return BadRequest("object already exists");
                }
                using (fs) { await Request.BodyReader.CopyToAsync(fs); }
                return Ok();
            } catch (Exception) {
                return BadRequest("transmission failed");
            }
        }

        // DELETE: api/v1/object/5.zip
        [HttpDelete("{id}")]
        public bool Delete(string name) {
            return _Idxr.Remove(name);
        }
    }
}
