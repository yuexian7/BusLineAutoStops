/* =======================================================================
   serve-site.js — 只在 127.0.0.1 起一个静态服务器看站点源码，不参与发布
   用法：node scripts/serve-site.js [端口]      默认 4173
   · 服务目录：D:\WorkSpace\CS2MOD\站点（源码，不是 dist）
   · 只监听回环，不对外；本地没有平台注入的 /__qoder_auth/*，这类接口一律 404。
   ======================================================================= */

const http = require("http");
const fs = require("fs");
const path = require("path");

const ROOT = "D:\\WorkSpace\\CS2MOD\\站点";
const port = Number(process.argv[2] || 4173);

const TYPES = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".svg": "image/svg+xml",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".png": "image/png",
  ".webp": "image/webp",
  ".ico": "image/x-icon",
};

http
  .createServer((req, res) => {
    let p = decodeURIComponent(req.url.split("?")[0]);
    if (p === "/") p = "/index.html";
    const file = path.join(ROOT, p.replace(/^[/\\]+/, ""));
    if (!file.startsWith(ROOT)) {
      res.writeHead(403).end("forbidden");
      return;
    }
    fs.readFile(file, (err, buf) => {
      if (err) {
        // 本地没有平台注入的 /__qoder_auth/*，这类请求就是 404，属正常
        res.writeHead(404, { "content-type": "text/plain; charset=utf-8" }).end("not found: " + p);
        console.log("404 " + p);
        return;
      }
      res.writeHead(200, { "content-type": TYPES[path.extname(file).toLowerCase()] || "application/octet-stream" });
      res.end(buf);
      console.log("200 " + p + "  " + buf.length + "B");
    });
  })
  .listen(port, "127.0.0.1", () => {
    console.log("站点源码预览： http://127.0.0.1:" + port + "/");
  });
