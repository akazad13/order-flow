# Keycloak for the production profile (spec 019): the optimized production build, for PostgreSQL.
# Build-time options are baked in here; runtime ones (hostname, TLS, database address and
# credentials) come from deploy/prod/docker-compose.prod.yml and its secrets file.
FROM quay.io/keycloak/keycloak:26.4.2 AS builder
ENV KC_DB=postgres \
    KC_HEALTH_ENABLED=true
RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/keycloak/keycloak:26.4.2
COPY --from=builder /opt/keycloak/ /opt/keycloak/
ENTRYPOINT ["/opt/keycloak/bin/kc.sh"]
