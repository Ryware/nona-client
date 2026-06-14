import assert from "node:assert/strict";
import test from "node:test";
import { createNonaClient, NonaClientError } from "../dist/index.js";
import { capture, jsonResponse } from "./helpers.mjs";

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

test("missing apiKey throws before request execution", async () => {
  const client = createNonaClient("https://nona.test", {
    fetch: async () => jsonResponse({ value: "enabled", contentType: "string" })
  });

  await assert.rejects(
    () => client.getConfigValue("production", "Features:Checkout"),
    (error) => {
      assert.equal(error instanceof Error, true);
      assert.equal(
        error.message,
        "Nona API-key calls require createNonaClient(...).apiKey."
      );
      return true;
    }
  );
});
