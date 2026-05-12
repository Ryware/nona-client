import { NonaClientError } from "./errors.js";
import type {
  NonaAuditLog,
  NonaClientOptions,
  NonaConfigEntry,
  NonaConfigValue,
  NonaCreateEnvironmentRequest,
  NonaCreateProjectRequest,
  NonaCreateUserRequest,
  NonaDashboardCounts,
  NonaEnvironment,
  NonaLoginResponse,
  NonaProject,
  NonaProjectAccess,
  NonaProjectAccessRequest,
  NonaRegisterResult,
  NonaRequestOptions,
  NonaRequestPasswordResetRequest,
  NonaRerollApiKeysRequest,
  NonaUpdateUserRequest,
  NonaUpsertConfigEntryRequest,
  NonaUser
} from "./types.js";

const API_KEY_HEADER_NAME = "X-Api-Key";

type AuthMode = "none" | "apiKey" | "bearer";

interface SendOptions extends NonaRequestOptions {
  auth: AuthMode;
  body?: unknown;
  method: string;
  path: string;
}

export class NonaClient {
  private readonly baseUrl: URL;
  private readonly fetchImpl: typeof fetch;
  private readonly defaultHeaders?: HeadersInit;

  public apiKey?: string;
  public bearerToken?: string;

  public constructor(baseUrl: string | URL, options?: Omit<NonaClientOptions, "baseUrl">);
  public constructor(options: NonaClientOptions);
  public constructor(
    baseUrlOrOptions: string | URL | NonaClientOptions,
    options: Omit<NonaClientOptions, "baseUrl"> = {}
  ) {
    const resolvedOptions =
      typeof baseUrlOrOptions === "string" || baseUrlOrOptions instanceof URL
        ? { ...options, baseUrl: baseUrlOrOptions }
        : baseUrlOrOptions;

    this.baseUrl = ensureTrailingSlash(new URL(resolvedOptions.baseUrl));
    this.apiKey = resolvedOptions.apiKey;
    this.bearerToken = resolvedOptions.bearerToken;
    this.defaultHeaders = resolvedOptions.defaultHeaders;
    this.fetchImpl = resolvedOptions.fetch ?? globalThis.fetch?.bind(globalThis);

    if (!this.fetchImpl) {
      throw new Error("NonaClient requires a fetch implementation.");
    }
  }

  public async getConfigValue(
    environmentId: string,
    key: string,
    options: NonaRequestOptions = {}
  ): Promise<NonaConfigValue> {
    return this.send<NonaConfigValue>({
      method: "GET",
      path: `api/${segment(environmentId, "environmentId")}/${segment(key, "key")}`,
      auth: "apiKey",
      ...options
    });
  }

  public async tryGetConfigValue(
    environmentId: string,
    key: string,
    options: NonaRequestOptions = {}
  ): Promise<NonaConfigValue | null> {
    try {
      return await this.getConfigValue(environmentId, key, options);
    } catch (error) {
      if (error instanceof NonaClientError && error.status === 404) {
        return null;
      }

      throw error;
    }
  }

  public async getStringValue(
    environmentId: string,
    key: string,
    options: NonaRequestOptions = {}
  ): Promise<string> {
    const configValue = await this.getConfigValue(environmentId, key, options);
    return configValue.value;
  }

  public async getJsonValue<T>(
    environmentId: string,
    key: string,
    options: NonaRequestOptions = {}
  ): Promise<T> {
    const configValue = await this.getConfigValue(environmentId, key, options);
    return JSON.parse(configValue.value) as T;
  }

  public async login(
    email: string,
    password: string,
    storeToken = true,
    options: NonaRequestOptions = {}
  ): Promise<NonaLoginResponse> {
    const response = await this.send<NonaLoginResponse>({
      method: "POST",
      path: "auth/login",
      auth: "none",
      body: { email, password },
      ...options
    });

    if (storeToken) {
      this.bearerToken = response.token;
    }

    return response;
  }

  public async register(
    email: string,
    password: string,
    storeToken = true,
    options: NonaRequestOptions = {}
  ): Promise<NonaRegisterResult> {
    const result = await this.send<NonaRegisterResult>({
      method: "POST",
      path: "auth/register",
      auth: "none",
      body: { email, password },
      ...options
    });

    if (storeToken && result.response) {
      this.bearerToken = result.response.token;
    }

    return result;
  }

  public async anyUsersExist(options: NonaRequestOptions = {}): Promise<boolean> {
    return this.send<boolean>({
      method: "GET",
      path: "auth/first-time",
      auth: "none",
      ...options
    });
  }

  public async requestPasswordReset(
    request: string | NonaRequestPasswordResetRequest,
    options: NonaRequestOptions = {}
  ): Promise<void> {
    const body = typeof request === "string" ? { email: request } : request;

    await this.sendNoContent({
      method: "POST",
      path: "auth/forgot-password",
      auth: "none",
      body,
      ...options
    });
  }

  public async listProjects(options: NonaRequestOptions = {}): Promise<NonaProject[]> {
    return this.send<NonaProject[]>({
      method: "GET",
      path: "admin/projects",
      auth: "bearer",
      ...options
    });
  }

  public async createProject(
    request: string | NonaCreateProjectRequest,
    options: NonaRequestOptions = {}
  ): Promise<NonaProject> {
    const body = typeof request === "string" ? { name: request } : request;

    return this.send<NonaProject>({
      method: "POST",
      path: "admin/projects",
      auth: "bearer",
      body,
      ...options
    });
  }

  public async deleteProject(projectId: string, options: NonaRequestOptions = {}): Promise<void> {
    await this.sendNoContent({
      method: "DELETE",
      path: `admin/projects/${segment(projectId, "projectId")}`,
      auth: "bearer",
      ...options
    });
  }

  public async rerollApiKeys(
    projectId: string,
    request: string | NonaRerollApiKeysRequest,
    options: NonaRequestOptions = {}
  ): Promise<NonaProject> {
    const body = typeof request === "string" ? { keyType: request } : request;

    return this.send<NonaProject>({
      method: "POST",
      path: `admin/projects/${segment(projectId, "projectId")}/reroll-keys`,
      auth: "bearer",
      body,
      ...options
    });
  }

  public async getDashboardCounts(options: NonaRequestOptions = {}): Promise<NonaDashboardCounts> {
    return this.send<NonaDashboardCounts>({
      method: "GET",
      path: "admin/dashboard/counts",
      auth: "bearer",
      ...options
    });
  }

  public async listAuditLogs(options: NonaRequestOptions = {}): Promise<NonaAuditLog[]> {
    return this.send<NonaAuditLog[]>({
      method: "GET",
      path: "admin/audit-logs",
      auth: "bearer",
      ...options
    });
  }

  public async listUsers(options: NonaRequestOptions = {}): Promise<NonaUser[]> {
    return this.send<NonaUser[]>({
      method: "GET",
      path: "admin/users",
      auth: "bearer",
      ...options
    });
  }

  public async getUser(id: number, options: NonaRequestOptions = {}): Promise<NonaUser> {
    return this.send<NonaUser>({
      method: "GET",
      path: `admin/users/${id}`,
      auth: "bearer",
      ...options
    });
  }

  public async createUser(
    request: NonaCreateUserRequest,
    options: NonaRequestOptions = {}
  ): Promise<NonaUser> {
    return this.send<NonaUser>({
      method: "POST",
      path: "admin/users",
      auth: "bearer",
      body: request,
      ...options
    });
  }

  public async updateUser(
    id: number,
    request: NonaUpdateUserRequest,
    options: NonaRequestOptions = {}
  ): Promise<NonaUser> {
    return this.send<NonaUser>({
      method: "PUT",
      path: `admin/users/${id}`,
      auth: "bearer",
      body: request,
      ...options
    });
  }

  public async deleteUser(id: number, options: NonaRequestOptions = {}): Promise<void> {
    await this.sendNoContent({
      method: "DELETE",
      path: `admin/users/${id}`,
      auth: "bearer",
      ...options
    });
  }

  public async getUserProjects(id: number, options: NonaRequestOptions = {}): Promise<NonaProjectAccess[]> {
    return this.send<NonaProjectAccess[]>({
      method: "GET",
      path: `admin/users/${id}/projects`,
      auth: "bearer",
      ...options
    });
  }

  public async setProjectAccess(
    id: number,
    projectName: string,
    request: string | NonaProjectAccessRequest,
    options: NonaRequestOptions = {}
  ): Promise<NonaProjectAccess> {
    const body = typeof request === "string" ? { role: request } : request;

    return this.send<NonaProjectAccess>({
      method: "PUT",
      path: `admin/users/${id}/projects/${segment(projectName, "projectName")}`,
      auth: "bearer",
      body,
      ...options
    });
  }

  public async removeProjectAccess(
    id: number,
    projectName: string,
    options: NonaRequestOptions = {}
  ): Promise<void> {
    await this.sendNoContent({
      method: "DELETE",
      path: `admin/users/${id}/projects/${segment(projectName, "projectName")}`,
      auth: "bearer",
      ...options
    });
  }

  public async listEnvironments(projectId: string, options: NonaRequestOptions = {}): Promise<NonaEnvironment[]> {
    return this.send<NonaEnvironment[]>({
      method: "GET",
      path: `admin/projects/${segment(projectId, "projectId")}/environments`,
      auth: "bearer",
      ...options
    });
  }

  public async createEnvironment(
    projectId: string,
    request: string | NonaCreateEnvironmentRequest,
    options: NonaRequestOptions = {}
  ): Promise<NonaEnvironment> {
    const body = typeof request === "string" ? { name: request } : request;

    return this.send<NonaEnvironment>({
      method: "POST",
      path: `admin/projects/${segment(projectId, "projectId")}/environments`,
      auth: "bearer",
      body,
      ...options
    });
  }

  public async deleteEnvironment(
    projectId: string,
    environmentId: string,
    options: NonaRequestOptions = {}
  ): Promise<void> {
    await this.sendNoContent({
      method: "DELETE",
      path: `admin/projects/${segment(projectId, "projectId")}/environments/${segment(environmentId, "environmentId")}`,
      auth: "bearer",
      ...options
    });
  }

  public async listConfigEntries(
    projectId: string,
    environmentName: string,
    options: NonaRequestOptions = {}
  ): Promise<NonaConfigEntry[]> {
    return this.send<NonaConfigEntry[]>({
      method: "GET",
      path:
        `admin/projects/${segment(projectId, "projectId")}` +
        `/environments/${segment(environmentName, "environmentName")}` +
        "/config-entries",
      auth: "bearer",
      ...options
    });
  }

  public async getConfigEntry(
    projectId: string,
    environmentName: string,
    key: string,
    options: NonaRequestOptions = {}
  ): Promise<NonaConfigEntry> {
    return this.send<NonaConfigEntry>({
      method: "GET",
      path:
        `admin/projects/${segment(projectId, "projectId")}` +
        `/environments/${segment(environmentName, "environmentName")}` +
        `/config-entries/${segment(key, "key")}`,
      auth: "bearer",
      ...options
    });
  }

  public async upsertConfigEntry(
    projectId: string,
    environmentName: string,
    key: string,
    request: NonaUpsertConfigEntryRequest,
    options: NonaRequestOptions = {}
  ): Promise<NonaConfigEntry> {
    return this.send<NonaConfigEntry>({
      method: "PUT",
      path:
        `admin/projects/${segment(projectId, "projectId")}` +
        `/environments/${segment(environmentName, "environmentName")}` +
        `/config-entries/${segment(key, "key")}`,
      auth: "bearer",
      body: request,
      ...options
    });
  }

  public async deleteConfigEntry(
    projectId: string,
    environmentName: string,
    key: string,
    options: NonaRequestOptions = {}
  ): Promise<void> {
    await this.sendNoContent({
      method: "DELETE",
      path:
        `admin/projects/${segment(projectId, "projectId")}` +
        `/environments/${segment(environmentName, "environmentName")}` +
        `/config-entries/${segment(key, "key")}`,
      auth: "bearer",
      ...options
    });
  }

  private async send<T>(options: SendOptions): Promise<T> {
    const response = await this.sendRequest(options);
    const responseBody = await response.text();

    if (!response.ok) {
      throwResponseError(response, options.method, response.url, responseBody);
    }

    if (!responseBody.trim()) {
      throw new NonaClientError(
        "Nona returned an empty response body.",
        response.status,
        options.method,
        response.url,
        responseBody
      );
    }

    try {
      return JSON.parse(responseBody) as T;
    } catch (error) {
      throw new NonaClientError(
        "Nona returned a response that could not be deserialized.",
        response.status,
        options.method,
        response.url,
        responseBody,
        error
      );
    }
  }

  private async sendNoContent(options: SendOptions): Promise<void> {
    const response = await this.sendRequest(options);

    if (!response.ok) {
      const responseBody = await response.text();
      throwResponseError(response, options.method, response.url, responseBody);
    }
  }

  private async sendRequest(options: SendOptions): Promise<Response> {
    const url = new URL(options.path.replace(/^\/+/, ""), this.baseUrl).toString();
    const headers = new Headers(this.defaultHeaders);
    headers.set("Accept", "application/json");
    applyAuthentication(headers, options.auth, this.apiKey, this.bearerToken);

    let body: string | undefined;
    if (options.body !== undefined) {
      headers.set("Content-Type", "application/json");
      body = JSON.stringify(options.body);
    }

    return this.fetchImpl(url, {
      method: options.method,
      headers,
      body,
      signal: options.signal
    });
  }
}

function applyAuthentication(headers: Headers, auth: AuthMode, apiKey?: string, bearerToken?: string): void {
  switch (auth) {
    case "none":
      return;
    case "apiKey":
      if (!apiKey?.trim()) {
        throw new Error("Nona API-key calls require NonaClient.apiKey.");
      }

      headers.set(API_KEY_HEADER_NAME, apiKey);
      return;
    case "bearer":
      if (!bearerToken?.trim()) {
        throw new Error("Nona admin calls require NonaClient.bearerToken.");
      }

      headers.set("Authorization", `Bearer ${bearerToken}`);
      return;
  }
}

function throwResponseError(response: Response, method: string, url: string, responseBody: string): never {
  const message = readErrorMessage(responseBody) ?? `Nona request failed with HTTP ${response.status} (${response.statusText}).`;
  throw new NonaClientError(message, response.status, method, url, responseBody);
}

function readErrorMessage(responseBody: string): string | undefined {
  if (!responseBody.trim()) {
    return undefined;
  }

  try {
    const parsed = JSON.parse(responseBody) as { error?: unknown; message?: unknown };
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
