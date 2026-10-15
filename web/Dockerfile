FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# Keycloak's address for the browser; the production profile passes its HTTPS one (spec 019).
ARG VITE_AUTH_AUTHORITY=http://localhost:8180/realms/orderflow
RUN npm run build

FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
