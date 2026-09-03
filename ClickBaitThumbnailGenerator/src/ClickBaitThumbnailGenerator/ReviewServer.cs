using System.Diagnostics;
using Microsoft.AspNetCore.Http.Json;

namespace ClickBaitThumbnailGenerator;

public sealed class ReviewServer(SqliteStore store, StorageOptions storage)
{
    public async Task RunAsync(int port, bool openBrowser, CancellationToken cancellationToken)
    {
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.Configure<JsonOptions>(options => options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
        var app = builder.Build();

        app.MapGet("/", () => Results.Content(Page, "text/html"));
        app.MapGet("/api/jobs", async (string? status, string? category, string? failure, CancellationToken token) =>
            Results.Ok(await store.GetJobsAsync(status, category, failure, token).ConfigureAwait(false)));
        app.MapGet("/api/stats", async (CancellationToken token) => Results.Ok(await store.GetStatisticsAsync(token).ConfigureAwait(false)));
        app.MapGet("/images/{filename}", (string filename) =>
        {
            var safeName = Path.GetFileName(filename);
            if (!string.Equals(filename, safeName, StringComparison.Ordinal) || !filename.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) return Results.NotFound();
            var fullPath = Path.GetFullPath(Path.Combine(storage.GeneratedPath, safeName));
            var root = Path.GetFullPath(storage.GeneratedPath) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(root, StringComparison.Ordinal) && File.Exists(fullPath)
                ? Results.File(fullPath, "image/webp", enableRangeProcessing: true)
                : Results.NotFound();
        });
        app.MapPost("/api/jobs/{id}/approve", async (string id, CancellationToken token) =>
        {
            await store.SetReviewAsync(id, ReviewStatus.Approved, cancellationToken: token).ConfigureAwait(false);
            return Results.NoContent();
        });
        app.MapPost("/api/jobs/{id}/reject", async (string id, CancellationToken token) =>
        {
            await store.SetReviewAsync(id, ReviewStatus.Rejected, cancellationToken: token).ConfigureAwait(false);
            return Results.NoContent();
        });
        app.MapPost("/api/jobs/{id}/regenerate", async (string id, CancellationToken token) =>
        {
            await store.ResetJobAsync(id, token).ConfigureAwait(false);
            return Results.NoContent();
        });
        app.MapPost("/api/jobs/{id}/flag-text", async (string id, CancellationToken token) =>
        {
            await store.SetReviewAsync(id, ReviewStatus.Pending, JobStatus.NeedsReview, token).ConfigureAwait(false);
            return Results.NoContent();
        });
        app.MapPost("/api/jobs/{id}/flag-duplicate", async (string id, CancellationToken token) =>
        {
            await store.SetReviewAsync(id, ReviewStatus.Pending, JobStatus.DuplicateSuspected, token).ConfigureAwait(false);
            return Results.NoContent();
        });
        app.MapPut("/api/jobs/{id}/scenario", async (string id, ScenarioEdit request, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(request.Scene) || request.Scene.Trim().Length is < 12 or > 300)
                return Results.BadRequest(new { error = "Scene must contain 12–300 characters." });
            try
            {
                await store.UpdateScenarioAndResetAsync(id, request.Scene, token).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (Microsoft.Data.Sqlite.SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                return Results.Conflict(new { error = "That scene duplicates an existing scenario." });
            }
        });

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var url = $"http://127.0.0.1:{port}";
            Console.WriteLine($"Review gallery: {url}");
            if (openBrowser) TryOpenBrowser(url);
        });
        await app.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsMacOS()) Process.Start("open", url);
            else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.WriteLine("Could not open a browser automatically; open the review URL manually.");
        }
    }

    private sealed record ScenarioEdit(string Scene);

    private const string Page = """
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>ClickBait Thumbnail Review</title><style>
:root{color-scheme:dark;font-family:Inter,ui-sans-serif,system-ui;background:#111020;color:#f8f4ff}*{box-sizing:border-box}
body{margin:0;background:radial-gradient(circle at 20% 0,#34245e,#111020 55%);min-height:100vh}header{position:sticky;top:0;z-index:2;padding:18px 24px;background:#17142eea;backdrop-filter:blur(12px);border-bottom:1px solid #ffffff20}
h1{margin:0 0 12px;font-size:clamp(22px,3vw,36px)}.toolbar,.counts,.actions{display:flex;gap:9px;flex-wrap:wrap;align-items:center}select,input,button,textarea{font:inherit;border-radius:9px;border:1px solid #ffffff35;background:#211c3e;color:#fff;padding:9px}button{cursor:pointer;font-weight:750}button:hover,button:focus{border-color:#76e8ff;outline:none}.approve{background:#117f58}.reject{background:#9a294e}.warn{background:#7e5321}.counts{margin-top:10px;color:#cfc8e8}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(290px,1fr));gap:18px;padding:24px}.card{background:#1c1833;border:1px solid #ffffff22;border-radius:16px;overflow:hidden;box-shadow:0 12px 36px #0006}.card.active{outline:3px solid #66e1ff}.card img,.placeholder{width:100%;aspect-ratio:16/9;object-fit:cover;background:#090815}.placeholder{display:grid;place-items:center;color:#8c84a7}.body{padding:14px}.scene{font-size:17px;font-weight:700;margin:0 0 8px}.meta,.reason{font-size:13px;color:#bdb5d7;margin:6px 0}.titles{margin:10px 0;padding-left:24px;color:#76e8ff;font-weight:700}.reason{color:#ffb3c2}.actions{margin-top:12px}.actions button{font-size:13px;padding:7px}.edit{width:100%;resize:vertical;margin-top:10px}.empty{padding:60px;text-align:center;color:#c7bedf}
</style></head><body><header><h1>Thumbnail review</h1><div class="toolbar"><select id="status"><option value="">All statuses</option><option>NeedsReview</option><option>DuplicateSuspected</option><option>Failed</option><option>Approved</option><option>Rejected</option><option>Pending</option></select><input id="category" placeholder="Category"><input id="failure" placeholder="Failure contains"><button onclick="load()">Filter</button></div><div class="counts" id="counts"></div></header><main class="grid" id="grid"></main>
<script>
let jobs=[],active=0;const grid=document.querySelector('#grid');
async function api(path,options){const r=await fetch(path,options);if(!r.ok){let x={};try{x=await r.json()}catch{}throw new Error(x.error||`Request failed (${r.status})`)}return r.status===204?null:r.json()}
async function load(){const q=new URLSearchParams();for(const id of ['status','category','failure']){const v=document.querySelector('#'+id).value;if(v)q.set(id,v)}jobs=await api('/api/jobs?'+q);active=Math.min(active,Math.max(0,jobs.length-1));render();const s=await api('/api/stats');document.querySelector('#counts').textContent=`Approved ${s.approved} · Rejected ${s.rejected} · Pending ${s.pending+s.needsReview+s.duplicateSuspected} · Failed ${s.failed}`}
function render(){if(!jobs.length){grid.innerHTML='<div class="empty">No thumbnails match these filters.</div>';return}grid.innerHTML=jobs.map((j,i)=>`<article class="card ${i===active?'active':''}" data-i="${i}">${j.finalFilename?`<img loading="lazy" src="/images/${encodeURIComponent(j.finalFilename)}?v=${encodeURIComponent(j.updatedAtUtc)}" alt="">`:'<div class="placeholder">No generated image</div>'}<div class="body"><p class="scene">${esc(j.scene)}</p><p class="meta">${esc(j.scenarioId)} · ${esc(j.category)} · ${esc(j.visualStyle)}<br>${esc(j.status)} / ${esc(j.reviewStatus)} · OCR ${esc(j.textDetectionResult||'not run')}</p>${j.aiTitles?.length?`<ol class="titles">${j.aiTitles.map(t=>`<li>${esc(t)}</li>`).join('')}</ol>`:'<p class="meta">AI distractor titles not generated yet.</p>'}${j.failureReason?`<p class="reason">${esc(j.failureReason)}</p>`:''}<div class="actions"><button class="approve" onclick="act('${j.scenarioId}','approve')">Approve</button><button class="reject" onclick="act('${j.scenarioId}','reject')">Reject</button><button onclick="act('${j.scenarioId}','regenerate')">Regenerate</button><button class="warn" onclick="act('${j.scenarioId}','flag-text')">Text</button><button class="warn" onclick="act('${j.scenarioId}','flag-duplicate')">Duplicate</button></div><textarea class="edit" id="e-${j.scenarioId}">${esc(j.scene)}</textarea><button onclick="edit('${j.scenarioId}')">Save & regenerate</button></div></article>`).join('');document.querySelector(`[data-i="${active}"]`)?.scrollIntoView({block:'nearest'})}
async function act(id,action){await api(`/api/jobs/${id}/${action}`,{method:'POST'});await load()}async function edit(id){try{await api(`/api/jobs/${id}/scenario`,{method:'PUT',headers:{'content-type':'application/json'},body:JSON.stringify({scene:document.querySelector('#e-'+id).value})});await load()}catch(e){alert(e.message)}}
function esc(v){return String(v??'').replace(/[&<>'"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[c]))}
document.addEventListener('click',e=>{const card=e.target.closest('.card');if(card){active=Number(card.dataset.i);render()}});document.addEventListener('keydown',e=>{if(['INPUT','TEXTAREA','SELECT'].includes(e.target.tagName))return;if(e.key==='ArrowRight'||e.key==='ArrowDown'){active=Math.min(jobs.length-1,active+1);render()}if(e.key==='ArrowLeft'||e.key==='ArrowUp'){active=Math.max(0,active-1);render()}const j=jobs[active];if(j&&e.key.toLowerCase()==='a')act(j.scenarioId,'approve');if(j&&e.key.toLowerCase()==='r')act(j.scenarioId,'reject');if(j&&e.key.toLowerCase()==='g')act(j.scenarioId,'regenerate')});load();
</script></body></html>
""";
}
