using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StacksAtlas.Core.Services.Network;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Data;
using StacksAtlas.API.Services;

namespace StacksAtlas.API.Controllers
{
    [Authorize(Roles = "Admin,Standard")]
    [ApiController]
    [Route("api/tools")]
    public class TracerouteController : ControllerBase
    {
        private readonly ITracerouteService _tracerouteService;
        private readonly IDeviceRepository _repo;
        private readonly RemoteExecutionService? _fedService;

        public TracerouteController(
            ITracerouteService tracerouteService,
            IDeviceRepository repo,
            RemoteExecutionService? fedService = null)
        {
            _tracerouteService = tracerouteService;
            _repo = repo;
            _fedService = fedService;
        }

        [HttpGet("traceroute")]
        public async Task<ActionResult<List<TracerouteHop>>> RunTraceroute(
            string target,
            Guid? deviceId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(target))
                return BadRequest(new { message = "Target IP is required." });

            var device = ResolveDevice(target, deviceId);

            if (device != null)
            {
                device.Status = "online";
                device.LastSeen = DateTime.UtcNow;
                _repo.UpsertDevice(device, true);
            }

            List<TracerouteHop> hops;

            if (device != null && !string.IsNullOrEmpty(device.NodeId) && _fedService != null)
            {
                var relay = await _fedService.RelayTracerouteForDeviceAsync(
                    device,
                    target,
                    cancellationToken);

                if (relay.Error != null)
                    return StatusCode(502, new { message = relay.Error });

                hops = relay.Hops;
            }
            else
            {
                hops = [];
                await foreach (var hop in _tracerouteService.RunTracerouteAsync(target).WithCancellation(cancellationToken))
                {
                    hops.Add(hop);
                }
            }

            if (hops.Count == 0)
            {
                return StatusCode(502, new
                {
                    message = "No hops returned. The target may be unreachable or ICMP may be blocked on this network path."
                });
            }

            return Ok(hops);
        }

        private Device? ResolveDevice(string target, Guid? deviceId)
        {
            if (deviceId.HasValue)
            {
                var byId = _repo.GetById(deviceId.Value);
                if (byId != null)
                    return byId;
            }

            return _repo.GetByIp(target);
        }
    }
}
