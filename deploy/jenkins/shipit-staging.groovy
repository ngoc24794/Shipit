/*
 * Self-contained Declarative Jenkins Pipeline for Shipit (Staging Only).
 * Paste directly into a Jenkins Pipeline job ("Pipeline script").
 *
 * Target environment:
 *   - Staging cluster context: staging-admin@staging
 *   - Namespace: devops (never targets production)
 *   - Deployment: shipit (container: app)
 *
 * Required Jenkins plugins:
 *   - Pipeline (Declarative)
 *   - Credentials Binding (withCredentials)
 *   - Docker Pipeline (docker.withRegistry)
 *   - Kubernetes CLI (withKubeConfig)
 *   - Build Name and Description Setter (buildName)
 *   - Workspace Cleanup (deleteDir)
 *
 * Required Jenkins credentials:
 *   - github-token                  (Secret text: GitHub personal access token)
 *   - private-docker-registry-creds (Username with password: Docker registry 192.168.1.241)
 *   - k8s-local                     (Secret file / kubeconfig with context staging-admin@staging)
 *
 * Agent requirements:
 *   - git, docker (with BuildKit support), kubectl, sed (envsubst is NOT needed)
 *   - Optional (only when RUN_TESTS=true): node/npm and dotnet
 *
 * Note on database migrations:
 *   - Shipit applies EF Core migrations automatically on startup via
 *     ShipitHost.PrepareDatabaseAsync() in src/Shipit.Api/ShipitHost.cs,
 *     so no separate DB migration stage is required.
 */

pipeline {
    agent any

    parameters {
        string(
            name: 'GIT_BRANCH',
            defaultValue: 'main',
            description: 'Git branch to clone and build'
        )
        string(
            name: 'IMAGE_TAG',
            defaultValue: '',
            description: 'Optional existing image tag (git short SHA) to deploy directly, skipping clone/test/build (rollback/redeploy)'
        )
        booleanParam(
            name: 'APPLY_MANIFESTS',
            defaultValue: false,
            description: 'Apply deploy/k8s/base/*.yaml before rollout (first-time install or manifest changes; never touches shipit-secrets)'
        )
        booleanParam(
            name: 'RUN_TESTS',
            defaultValue: false,
            description: 'Run web and backend tests before docker build (ignored when IMAGE_TAG is set)'
        )
    }

    options {
        disableConcurrentBuilds()
        timeout(time: 40, unit: 'MINUTES')
    }

    environment {
        CI_DOCKER_REGISTRY = '192.168.1.241'
        CI_IMAGE_NAME      = 'shipit'
        CI_DEPLOYMENT      = 'shipit'
        CI_SERVICE         = 'shipit'
        CI_CONTAINER_NAME  = 'app'
        CI_NAMESPACE       = 'devops'
        K8S_CONTEXT        = 'staging-admin@staging'
        CI_DOCKERFILE      = 'Dockerfile'
        CI_SECRET_NAME     = 'shipit-secrets'
        GIT_CREDENTIAL_ID  = 'github-token'
        REGISTRY_CRED_ID   = 'private-docker-registry-creds'
        KUBE_CRED_ID       = 'k8s-local'
        GIT_OWNER          = 'ngoc24794'
        GIT_REPOSITORY     = 'Shipit'
    }

    stages {
        stage('Initialization') {
            steps {
                script {
                    if (env.K8S_CONTEXT != 'staging-admin@staging' || env.CI_NAMESPACE != 'devops') {
                        error "Refusing to run: staging-only pipeline requires K8S_CONTEXT=staging-admin@staging and CI_NAMESPACE=devops"
                    }

                    env.CURRENT_STAGE = 'initialization'
                    env.APPLY_BASE_MANIFESTS = params.APPLY_MANIFESTS ? 'true' : 'false'

                    def overrideTag = params.IMAGE_TAG?.trim()
                    if (overrideTag) {
                        env.SKIP_BUILD          = 'true'
                        env.CI_APP_VERSION      = overrideTag
                        env.CI_DOCKER_IMAGE_TAG = "${env.CI_DOCKER_REGISTRY}/${env.CI_IMAGE_NAME}:${env.CI_APP_VERSION}"
                        safeBuildName("shipit@${params.GIT_BRANCH}:${env.CI_APP_VERSION}")
                    } else {
                        env.SKIP_BUILD          = 'false'
                        env.CI_APP_VERSION      = ''
                        env.CI_DOCKER_IMAGE_TAG = ''
                        safeBuildName("shipit@${params.GIT_BRANCH}")
                    }
                }
            }
        }

        stage('Clone') {
            when {
                expression { env.SKIP_BUILD != 'true' || params.APPLY_MANIFESTS }
            }
            steps {
                script {
                    env.CURRENT_STAGE = 'clone'
                }
                deleteDir()
                withCredentials([string(credentialsId: env.GIT_CREDENTIAL_ID, variable: 'GIT_TOKEN')]) {
                    sh '''
                        set -eu
                        git clone --depth 1 --single-branch --no-tags --branch "$GIT_BRANCH" \
                            "https://x-access-token:${GIT_TOKEN}@github.com/${GIT_OWNER}/${GIT_REPOSITORY}.git"
                    '''
                }
            }
        }

        stage('Test') {
            when {
                expression { env.SKIP_BUILD != 'true' && params.RUN_TESTS }
            }
            steps {
                dir(env.GIT_REPOSITORY) {
                    script {
                        env.CURRENT_STAGE = 'test'
                    }
                    sh '''
                        set -eu
                        if command -v node >/dev/null 2>&1 && command -v npm >/dev/null 2>&1; then
                            (
                                cd web
                                env -u AVINA_STAGING_SHIPIT_DB_URL npm ci
                                env -u AVINA_STAGING_SHIPIT_DB_URL npm run typecheck
                                env -u AVINA_STAGING_SHIPIT_DB_URL npm test
                            )
                        else
                            echo "Skipping web tests: node/npm not found on build agent"
                        fi

                        if command -v dotnet >/dev/null 2>&1; then
                            env -u AVINA_STAGING_SHIPIT_DB_URL dotnet test Shipit.sln
                        else
                            echo "Skipping backend tests: dotnet not found on build agent"
                        fi
                    '''
                }
            }
        }

        stage('Build & Push') {
            when {
                expression { env.SKIP_BUILD != 'true' }
            }
            steps {
                dir(env.GIT_REPOSITORY) {
                    script {
                        env.CURRENT_STAGE = 'build'

                        env.CI_APP_VERSION      = sh(returnStdout: true, script: 'git rev-parse --short HEAD').trim()
                        env.CI_DOCKER_IMAGE_TAG = "${env.CI_DOCKER_REGISTRY}/${env.CI_IMAGE_NAME}:${env.CI_APP_VERSION}"
                        safeBuildName("shipit@${params.GIT_BRANCH}:${env.CI_APP_VERSION}")

                        docker.withRegistry("http://${env.CI_DOCKER_REGISTRY}", env.REGISTRY_CRED_ID) {
                            sh 'DOCKER_BUILDKIT=1 docker build -f "$CI_DOCKERFILE" -t "$CI_DOCKER_IMAGE_TAG" .'
                            sh 'docker push "$CI_DOCKER_IMAGE_TAG"'
                        }
                    }
                }
            }
        }

        stage('Deploy') {
            steps {
                script {
                    env.CURRENT_STAGE = 'deploy'
                }
                withKubeConfig([credentialsId: env.KUBE_CRED_ID, contextName: env.K8S_CONTEXT]) {
                    sh '''
                        set -eu

                        if [ "$K8S_CONTEXT" != "staging-admin@staging" ] || [ "$CI_NAMESPACE" != "devops" ]; then
                            echo "ERROR: Refusing to deploy outside staging (staging-admin@staging / devops)" >&2
                            exit 1
                        fi

                        if ! kubectl -n "$CI_NAMESPACE" get secret "$CI_SECRET_NAME" -o name >/dev/null 2>&1; then
                            echo "ERROR: Required secret $CI_SECRET_NAME not found in namespace $CI_NAMESPACE." >&2
                            echo "Create it out of band with scripts/create-secrets.ps1 before deploying." >&2
                            exit 1
                        fi

                        if [ "$APPLY_BASE_MANIFESTS" = "true" ]; then
                            for manifest in \
                                "${GIT_REPOSITORY}/deploy/k8s/base/00-namespace.yaml" \
                                "${GIT_REPOSITORY}/deploy/k8s/base/10-rbac-devops.yaml" \
                                "${GIT_REPOSITORY}/deploy/k8s/base/11-rbac-avina-workspace.yaml" \
                                "${GIT_REPOSITORY}/deploy/k8s/base/20-deployment.yaml" \
                                "${GIT_REPOSITORY}/deploy/k8s/base/30-service.yaml"; do
                                sed -E "s#(image:[[:space:]]*${CI_DOCKER_REGISTRY}/${CI_IMAGE_NAME}):.*#\\1:${CI_APP_VERSION}#" "$manifest" \
                                    | kubectl apply -f -
                            done
                        else
                            kubectl -n "$CI_NAMESPACE" set image "deployment/$CI_DEPLOYMENT" "$CI_CONTAINER_NAME=$CI_DOCKER_IMAGE_TAG"
                        fi

                        if ! kubectl -n "$CI_NAMESPACE" rollout status "deployment/$CI_DEPLOYMENT" --timeout=300s; then
                            echo "Rollout failed for $CI_DEPLOYMENT, rolling back" >&2
                            kubectl -n "$CI_NAMESPACE" rollout undo "deployment/$CI_DEPLOYMENT"
                            exit 1
                        fi
                    '''
                }
            }
        }

        stage('Smoke Check') {
            steps {
                script {
                    env.CURRENT_STAGE = 'smoke-check'
                }
                withKubeConfig([credentialsId: env.KUBE_CRED_ID, contextName: env.K8S_CONTEXT]) {
                    sh '''
                        set -eu
                        RUNNING_IMAGE=$(kubectl -n "$CI_NAMESPACE" get deployment "$CI_DEPLOYMENT" \
                            -o jsonpath='{.spec.template.spec.containers[?(@.name=="app")].image}')
                        if [ "$RUNNING_IMAGE" != "$CI_DOCKER_IMAGE_TAG" ]; then
                            echo "ERROR: Expected image $CI_DOCKER_IMAGE_TAG, got $RUNNING_IMAGE" >&2
                            exit 1
                        fi

                        HEALTHZ=$(kubectl get --raw "/api/v1/namespaces/${CI_NAMESPACE}/services/${CI_SERVICE}:80/proxy/healthz")
                        READYZ=$(kubectl get --raw "/api/v1/namespaces/${CI_NAMESPACE}/services/${CI_SERVICE}:80/proxy/readyz")

                        echo "=== Shipit Staging Rollout Summary ==="
                        echo "Context:    $K8S_CONTEXT"
                        echo "Namespace:  $CI_NAMESPACE"
                        echo "Deployment: $CI_DEPLOYMENT"
                        echo "Image:      $RUNNING_IMAGE"
                        echo "/healthz:   $HEALTHZ"
                        echo "/readyz:    $READYZ"
                    '''
                }
            }
        }
    }

    post {
        always {
            sh '''
                if [ "$SKIP_BUILD" != "true" ]; then
                    [ -n "${CI_DOCKER_IMAGE_TAG:-}" ] && docker image rm -f "$CI_DOCKER_IMAGE_TAG" || true
                fi
            '''
            deleteDir()
        }
    }
}

def safeBuildName(String name) {
    try {
        buildName(name)
    } catch (Exception e) {
        echo "buildName(${name}) skipped: ${e.message}"
    }
}
