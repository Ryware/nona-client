# @ryware/nona-client

JavaScript client for reading Nona configuration values.

## Install

```bash
npm install @ryware/nona-client
```

## Import

This package is ESM-only, so use `import`:

```js
import { createNonaClient, NonaClientError } from "@ryware/nona-client";
```

## Create a client

```js
const nona = createNonaClient({
  baseUrl: "https://nona.example.com",
  apiKey: "your-api-key"
});
```

You can also pass the base URL as the first argument:

```js
const nona = createNonaClient("https://nona.example.com", {
  apiKey: "your-api-key"
});
```

## Read config values

```js
const value = await nona.getConfigValue("production", "Features:Checkout");
console.log(value.value);
console.log(value.contentType);
```

If you only want the string value:

```js
const checkoutEnabled = await nona.getStringValue("production", "Features:Checkout");
```

If the value contains JSON:

```js
const settings = await nona.getJsonValue("production", "App:Settings");
console.log(settings);
```

If a key might not exist, use `tryGetConfigValue`:

```js
const maybeValue = await nona.tryGetConfigValue("production", "Missing:Key");

if (maybeValue === null) {
  console.log("No value found");
}
```

## Handle errors

Requests that fail with an HTTP error throw `NonaClientError`:

```js
try {
  await nona.getConfigValue("production", "Missing:Key");
} catch (error) {
  if (error instanceof NonaClientError) {
    console.error(error.status);
    console.error(error.message);
    console.error(error.responseBody);
    return;
  }

  throw error;
}
```

## Options

`createNonaClient` accepts these options:

- `baseUrl`: the Nona server URL
- `apiKey`: API key for config reads
- `fetch`: custom fetch implementation
- `defaultHeaders`: headers added to every request

## Runtime requirements

- Node.js 18 or newer
- Or any environment that provides `fetch`, `Headers`, and `Response`
