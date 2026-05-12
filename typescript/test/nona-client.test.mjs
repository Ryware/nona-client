import assert from "node:assert/strict";
import test from "node:test";
import { NonaClient, NonaClientError } from "../dist/index.js";

test("getConfigValue sends API key and parses the value", async () => {
  const calls = [];
  const client = new NonaClient("https://nona.test", {
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

test("login stores bearer token for admin calls", async () => {
  const calls = [];
  const client = new NonaClient("https://nona.test", {
    fetch: async (url, init) => {
      calls.push(capture(url, init));

      if (url.endsWith("/auth/login")) {
        return jsonResponse({
          token: "jwt-token",
          username: "admin@example.com",
          role: "viewer",
          expiresAt: "2026-05-11T10:00:00Z"
        });
      }

      return jsonResponse([]);
    }
  });

  await client.login("admin@example.com", "password");
  await client.listProjects();

  assert.equal(calls[0].url, "https://nona.test/auth/login");
  assert.equal(calls[0].headers.get("Authorization"), null);
  assert.equal(calls[1].url, "https://nona.test/admin/projects");
  assert.equal(calls[1].headers.get("Authorization"), "Bearer jwt-token");
});

test("failed requests throw NonaClientError with backend error message", async () => {
  const client = new NonaClient("https://nona.test", {
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
