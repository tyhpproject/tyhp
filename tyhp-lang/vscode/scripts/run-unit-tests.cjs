const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");

function collect(dir, acc) {
    if (!fs.existsSync(dir)) {
        return acc;
    }
    for (const ent of fs.readdirSync(dir, { withFileTypes: true })) {
        const p = path.join(dir, ent.name);
        if (ent.isDirectory()) {
            collect(p, acc);
        } else if (ent.name.endsWith(".test.js")) {
            acc.push(p);
        }
    }
    return acc;
}

const files = collect(path.join(__dirname, "..", "out"), []);
if (files.length === 0) {
    console.error("No compiled unit tests under out/. Run npm run compile first.");
    process.exit(1);
}

const result = spawnSync(process.execPath, ["--test", ...files], { stdio: "inherit" });
process.exit(result.status === null ? 1 : result.status);
