import assert from "node:assert/strict";
import test from "node:test";
import { createNonaClient, NonaClientError } from "../dist/index.js";

if (typeof globalThis.Headers === "undefined") {
  globalThis.Headers = class HeadersShim {
    #values = new Map();

    constructor(init = undefined) {
      if (!init) {
        return;
      }

      if (Array.isArray(init)) {
        for (const [key, value] of init) {
          this.set(key, value);
        }

        return;
      }

      if (typeof init.forEach === "function") {
        init.forEach((value, key) => {
          this.set(key, value);
        });
        return;
      }

      for (const [key, value] of Object.entries(init)) {
        this.set(key, value);
      }
    }

    set(key, value) {
      this.#values.set(String(key).toLowerCase(), String(value));
    }

    get(key) {
      return this.#values.get(String(key).toLowerCase()) ?? null;
    }
  };
}

if (typeof globalThis.Response === "undefined") {
  globalThis.Response = class ResponseShim {
    constructor(body, init = {}) {
      this._body = body ?? "";
      this.status = init.status ?? 200;
      this.statusText = init.statusText ?? "";
      this.headers = new Headers(init.headers);
      this.ok = this.status >= 200 && this.status < 300;
      this.url = init.url ?? "https://nona.test";
    }

    async text() {
      return typeof this._body === "string" ? this._body : String(this._body);
    }
  };
}

test("getConfigValue sends API key and parses the value", async () => {
  const calls = [];
  const client = createNonaClient("https://nona.test", {
    apiKey: "api-key",
    fetch: async (url, init) => {
      calls.push(capture(url, init));
      return jsonResponse({ value: "enabled", contentType: "string" });
    }
  });

  const value = await client.getConfigValue("production", "Features:Checkout");

  assert.equal(value.value, "enabled");
  assert.equal(value.contentType, "string");
  assert.equal(calls[0].url, "https://nona.test/api/production/Features%3ACheckout");
  assert.equal(calls[0].headers.get("X-Api-Key"), "api-key");
});

test("failed requests throw NonaClientError with backend error message", async () => {
  const client = createNonaClient("https://nona.test", {
    apiKey: "api-key",
    fetch: async () => jsonResponse({ error: "Config entry not found" }, 404)
  });

  await assert.rejects(
    () => client.getConfigValue("production", "missing"),
    error => {
      assert.ok(error instanceof NonaClientError);
      assert.equal(error.status, 404);
      assert.equal(error.message, "Config entry not found");
      return true;
    }
  );
});

test("apiKey can be set after client creation", async () => {
  const calls = [];
  const client = createNonaClient("https://nona.test", {
    fetch: async (url, init) => {
      calls.push(capture(url, init));
      return jsonResponse({ value: "enabled", contentType: "string" });
    }
  });

  client.apiKey = "late-key";
  await client.getConfigValue("production", "Features:Checkout");

  assert.equal(calls[0].headers.get("X-Api-Key"), "late-key");
});

function jsonResponse(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "Content-Type": "application/json"
    }
  });
}

function capture(url, init) {
  return {
    url,
    method: init?.method,
    headers: new Headers(init?.headers),
    body: init?.body
  };
}
