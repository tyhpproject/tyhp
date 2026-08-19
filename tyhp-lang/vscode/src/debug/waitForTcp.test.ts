import assert from "node:assert/strict";
import * as net from "node:net";
import { test } from "node:test";
import { probeTcpPort } from "./waitForTcp";

test("probeTcpPort is true for a listening server and false for a closed port", async () => {
    const server = net.createServer();
    await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", () => resolve()));
    const address = server.address();
    assert.ok(address && typeof address === "object");
    const port = address.port;
    try {
        assert.equal(await probeTcpPort("127.0.0.1", port, 500), true);
    } finally {
        await new Promise<void>((resolve, reject) =>
            server.close((err) => (err ? reject(err) : resolve()))
        );
    }
    assert.equal(await probeTcpPort("127.0.0.1", port, 200), false);
    assert.equal(await probeTcpPort("127.0.0.1", 0, 200), false);
});
