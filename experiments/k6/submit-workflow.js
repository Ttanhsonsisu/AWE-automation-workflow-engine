import http from 'k6/http';
import { check, sleep } from 'k6';
import exec from 'k6/execution';
import { Counter, Rate, Trend } from 'k6/metrics';

const baseUrl = (__ENV.BASE_URL || 'http://localhost:8080').replace(/\/$/, '');
const staticToken = __ENV.TOKEN || '';
const keycloakBaseUrl = (__ENV.KEYCLOAK_URL || 'http://localhost:8081').replace(/\/$/, '');
const keycloakRealm = __ENV.KEYCLOAK_REALM || 'awe-auth';
const tokenUrl =
  __ENV.KEYCLOAK_TOKEN_URL ||
  `${keycloakBaseUrl}/realms/${keycloakRealm}/protocol/openid-connect/token`;
const keycloakClientId = __ENV.KEYCLOAK_CLIENT_ID || 'test';
const keycloakClientSecret = __ENV.KEYCLOAK_CLIENT_SECRET || '';
const keycloakUsername = __ENV.KEYCLOAK_USERNAME || '';
const keycloakPassword = __ENV.KEYCLOAK_PASSWORD || '';
const tokenRefreshSkewSeconds = Number(__ENV.TOKEN_REFRESH_SKEW_SECONDS || 15);
const definitionId = __ENV.DEFINITION_ID || '';
const pollCompletion = (__ENV.POLL_COMPLETION || 'false').toLowerCase() === 'true';
const pollTimeoutSeconds = Number(__ENV.POLL_TIMEOUT_SECONDS || 60);
const pollIntervalSeconds = Number(__ENV.POLL_INTERVAL_SECONDS || 1);
const thinkTimeSeconds = Number(__ENV.THINK_TIME_SECONDS || 0);

let cachedToken = '';
let cachedTokenExpiresAt = 0;

export const submitLatency = new Trend('workflow_submit_latency');
export const completionLatency = new Trend('workflow_completion_latency');
export const submitted = new Counter('workflow_submitted');
export const completed = new Counter('workflow_completed');
export const failed = new Counter('workflow_failed');
export const submitErrors = new Rate('workflow_submit_errors');
export const completionTimeouts = new Rate('workflow_completion_timeouts');

export const options = {
  scenarios: {
    submit_workflows: {
      executor: 'constant-vus',
      vus: Number(__ENV.VUS || 10),
      duration: __ENV.DURATION || '1m',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.05'],
    workflow_submit_errors: ['rate<0.05'],
    workflow_submit_latency: ['p(95)<1000'],
  },
};

export function setup() {
  if (!definitionId) {
    throw new Error('DEFINITION_ID is required.');
  }

  if (!staticToken && (!keycloakUsername || !keycloakPassword)) {
    throw new Error(
      'TOKEN or KEYCLOAK_USERNAME/KEYCLOAK_PASSWORD is required because the workflow API is protected by [Authorize].',
    );
  }
}

export default function () {
  const iteration = exec.scenario.iterationInTest;
  const score = Math.floor(Math.random() * 100);
  const startedAt = Date.now();

  const response = http.post(
    `${baseUrl}/api/workflows`,
    JSON.stringify({
      definitionId,
      jobName: `k6-workflow-${iteration}`,
      inputData: {
        message: `load-test-${iteration}`,
        score,
      },
      isTest: true,
    }),
    { headers: headers() },
  );

  submitLatency.add(response.timings.duration);

  const accepted = check(response, {
    'submit status is 200': (r) => r.status === 200,
    'submit has instance id': (r) => Boolean(readInstanceId(r)),
  });

  submitErrors.add(!accepted);
  if (!accepted) {
    return;
  }

  submitted.add(1);

  const instanceId = readInstanceId(response);
  if (pollCompletion && instanceId) {
    pollUntilTerminal(instanceId, startedAt);
  }

  if (thinkTimeSeconds > 0) {
    sleep(thinkTimeSeconds);
  }
}

function pollUntilTerminal(instanceId, startedAt) {
  const deadline = Date.now() + pollTimeoutSeconds * 1000;

  while (Date.now() < deadline) {
    const response = http.get(`${baseUrl}/api/executions/${instanceId}`, { headers: headers() });
    const status = readStatus(response);

    if (isCompleted(status)) {
      completed.add(1);
      completionLatency.add(Date.now() - startedAt);
      return;
    }

    if (isFailed(status)) {
      failed.add(1);
      completionLatency.add(Date.now() - startedAt);
      return;
    }

    sleep(pollIntervalSeconds);
  }

  completionTimeouts.add(true);
}

function headers() {
  return {
    Authorization: `Bearer ${getAccessToken()}`,
    'Content-Type': 'application/json',
  };
}

function getAccessToken() {
  if (staticToken && (!keycloakUsername || !keycloakPassword)) {
    return staticToken;
  }

  const nowSeconds = Math.floor(Date.now() / 1000);
  if (cachedToken && cachedTokenExpiresAt - tokenRefreshSkewSeconds > nowSeconds) {
    return cachedToken;
  }

  const fields = {
    grant_type: 'password',
    client_id: keycloakClientId,
    username: keycloakUsername,
    password: keycloakPassword,
  };

  if (keycloakClientSecret) {
    fields.client_secret = keycloakClientSecret;
  }

  const response = http.post(tokenUrl, formEncode(fields), {
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    tags: { endpoint: 'keycloak_token' },
  });

  if (response.status !== 200) {
    throw new Error(`Unable to obtain Keycloak token. Status=${response.status}`);
  }

  const accessToken = response.json('access_token');
  if (!accessToken) {
    throw new Error('Keycloak token response does not contain access_token.');
  }

  cachedToken = accessToken;
  cachedTokenExpiresAt = nowSeconds + Number(response.json('expires_in') || 60);
  return cachedToken;
}

function formEncode(fields) {
  return Object.keys(fields)
    .filter((key) => fields[key] !== undefined && fields[key] !== null && fields[key] !== '')
    .map((key) => `${encodeURIComponent(key)}=${encodeURIComponent(fields[key])}`)
    .join('&');
}

function readInstanceId(response) {
  try {
    return response.json('data.instanceId') || response.json('data.InstanceId');
  } catch {
    return null;
  }
}

function readStatus(response) {
  try {
    return response.json('data.status') ?? response.json('data.Status');
  } catch {
    return null;
  }
}

function isCompleted(status) {
  return status === 'Completed' || status === 2;
}

function isFailed(status) {
  return status === 'Failed' || status === 'Cancelled' || status === 3 || status === 6;
}
