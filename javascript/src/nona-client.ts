import { NonaClientError } from "./errors.js";
import type {
  NonaClientOptions,
  NonaConfigValue,
  NonaRequestOptions,
} from "./types.js";

const API_KEY_HEADER_NAME = "X-Api-Key";

interface SendOptions extends NonaRequestOptions {
  body?: unknown;
  method: string;
  path: string;
}

export interface NonaClient {
  apiKey?: string;
  getConfigValue(environmentId: string, key: string, options?: NonaRequestOptions): Promise<NonaConfigValue>;
  tryGetConfigValue(environmentId: string, key: string, options?: NonaRequestOptions): Promise<NonaConfigValue | null>;
  getStringValue(environmentId: string, key: string, options?: NonaRequestOptions): Promise<string>;
  getJsonValue<T>(environmentId: string, key: string, options?: NonaRequestOptions): Promise<T>;
}

export function createNonaClient(
  baseUrl: string | URL,
  options?: Omit<NonaClientOptions, "baseUrl">,
): NonaClient;
export function createNonaClient(options: NonaClientOptions): NonaClient;
export function createNonaClient(
  baseUrlOrOptions: string | URL | NonaClientOptions,
  options: Omit<NonaClientOptions, "baseUrl"> = {},
): NonaClient {
  const resolvedOptions =
    typeof baseUrlOrOptions === "string" || baseUrlOrOptions instanceof URL
      ? { ...options, baseUrl: baseUrlOrOptions }
      : baseUrlOrOptions;

  const baseUrl = ensureTrailingSlash(new URL(resolvedOptions.baseUrl));
  const defaultHeaders = resolvedOptions.defaultHeaders;
  const fetchImpl = resolvedOptions.fetch ?? globalThis.fetch?.bind(globalThis);
  let apiKey = resolvedOptions.apiKey;

  if (!fetchImpl) {
    throw new Error("createNonaClient requires a fetch implementation.");
  }

  async function send<T>(request: SendOptions): Promise<T> {
    const response = await sendRequest(request);
    const responseBody = await response.text();

    if (!response.ok) {
      throwResponseError(response, request.method, response.url, responseBody);
    }

    if (!responseBody.trim()) {
      throw new NonaClientError(
        "Nona returned an empty response body.",
        response.status,
        request.method,
        response.url,
        responseBody,
      );
    }

    try {
      return JSON.parse(responseBody) as T;
    } catch (error) {
      throw new NonaClientError(
        "Nona returned a response that could not be deserialized.",
        response.status,
        request.method,
        response.url,
        responseBody,
        error,
      );
    }
  }

  async function sendRequest(request: SendOptions): Promise<Response> {
    const url = new URL(request.path.replace(/^\/+/, ""), baseUrl).toString();
    const headers = new Headers(defaultHeaders);
    headers.set("Accept", "application/json");
    applyAuthentication(headers, apiKey);

    let body: string | undefined;
    if (request.body !== undefined) {
      headers.set("Content-Type", "application/json");
      body = JSON.stringify(request.body);
    }

    return fetchImpl(url, {
      method: request.method,
      headers,
      body,
      signal: request.signal,
    });
  }

  return {
    async getConfigValue(
      environmentId: string,
      key: string,
      requestOptions: NonaRequestOptions = {},
    ): Promise<NonaConfigValue> {
      return send<NonaConfigValue>({
        method: "GET",
        path: `api/${segment(environmentId, "environmentId")}/${segment(key, "key")}`,
        ...requestOptions,
      });
    },
    async tryGetConfigValue(
      environmentId: string,
      key: string,
      requestOptions: NonaRequestOptions = {},
    ): Promise<NonaConfigValue | null> {
      try {
        return await this.getConfigValue(environmentId, key, requestOptions);
      } catch (error) {
        if (error instanceof NonaClientError && error.status === 404) {
          return null;
        }

        throw error;
      }
    },
    async getStringValue(
      environmentId: string,
      key: string,
      requestOptions: NonaRequestOptions = {},
    ): Promise<string> {
      const configValue = await this.getConfigValue(environmentId, key, requestOptions);
      return configValue.value;
    },
    async getJsonValue<T>(
      environmentId: string,
      key: string,
      requestOptions: NonaRequestOptions = {},
    ): Promise<T> {
      const configValue = await this.getConfigValue(environmentId, key, requestOptions);
      return JSON.parse(configValue.value) as T;
    },
  };
}

function applyAuthentication(headers: Headers, apiKey?: string): void {
  if (!apiKey?.trim()) {
    throw new Error("Nona API-key calls require createNonaClient(...).apiKey.");
  }

  headers.set(API_KEY_HEADER_NAME, apiKey);
  return;
}

function throwResponseError(
  response: Response,
  method: string,
  url: string,
  responseBody: string,
): never {
  const message =
    readErrorMessage(responseBody) ??
    `Nona request failed with HTTP ${response.status} (${response.statusText}).`;
  throw new NonaClientError(
    message,
    response.status,
    method,
    url,
    responseBody,
  );
}

function readErrorMessage(responseBody: string): string | undefined {
  if (!responseBody.trim()) {
    return undefined;
  }

  try {
    const parsed = JSON.parse(responseBody) as {
      error?: unknown;
      message?: unknown;
    };
    if (typeof parsed.error === "string") {
      return parsed.error;
    }

    if (typeof parsed.message === "string") {
      return parsed.message;
    }
  } catch {
    return undefined;
  }

  return undefined;
}

function segment(value: string, parameterName: string): string {
  if (!value?.trim()) {
    throw new Error(`${parameterName} cannot be empty.`);
  }

  return encodeURIComponent(value);
}

function ensureTrailingSlash(url: URL): URL {
  const value = url.toString();
  return value.endsWith("/") ? url : new URL(`${value}/`);
}
