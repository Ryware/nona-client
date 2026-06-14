import { NonaClientError } from "./errors.js";

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
