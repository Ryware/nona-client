import { NonaClientError } from "./errors.js";
import type { NonaConfigValue } from "./types.js";

const contentTypeHeaderName = "ContentType";

export async function readConfigValueResponse(
  response: Response,
  method: string,
  url: string,
): Promise<NonaConfigValue> {
  const responseBody = await response.text();

  if (!response.ok) {
    throwResponseError(response, method, url, responseBody);
  }

  const contentType = response.headers.get(contentTypeHeaderName);
  if (contentType) {
    return {
      value: responseBody,
      contentType,
    };
  }

  if (responseBody.trim()) {
    try {
      return parseLegacyConfigValue(responseBody);
    } catch (error) {
      throw new NonaClientError(
        "Nona returned a response that could not be deserialized.",
        response.status,
        method,
        url,
        responseBody,
        error,
      );
    }
  }

  throw new NonaClientError(
    `Nona returned a successful response without a '${contentTypeHeaderName}' header.`,
    response.status,
    method,
    url,
    responseBody,
  );
}

export async function readJsonResponse<T>(
  response: Response,
  method: string,
  url: string,
): Promise<T> {
  const responseBody = await response.text();

  if (!response.ok) {
    throwResponseError(response, method, url, responseBody);
  }

  if (!responseBody.trim()) {
    throw new NonaClientError(
      "Nona returned an empty response body.",
      response.status,
      method,
      url,
      responseBody,
    );
  }

  try {
    return JSON.parse(responseBody) as T;
  } catch (error) {
    throw new NonaClientError(
      "Nona returned a response that could not be deserialized.",
      response.status,
      method,
      url,
      responseBody,
      error,
    );
  }
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

function parseLegacyConfigValue(responseBody: string): NonaConfigValue {
  const parsed = JSON.parse(responseBody) as {
    value?: unknown;
    contentType?: unknown;
  };

  if (typeof parsed.value !== "string") {
    throw new Error("The response JSON must include a string 'value' property.");
  }

  if (typeof parsed.contentType !== "string") {
    throw new Error(
      "The response JSON must include a string 'contentType' property.",
    );
  }

  return {
    value: parsed.value,
    contentType: parsed.contentType,
  };
}
