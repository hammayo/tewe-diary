# Frontend image: build with Vite, serve the static output with nginx.
# Build from the repo root or from frontend/ (the context is frontend/): `docker build frontend`
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --ignore-scripts

# VITE_* values are baked into the bundle at build time.
ARG VITE_API_URL
ENV VITE_API_URL=${VITE_API_URL}
COPY . .
RUN npm run build

FROM nginxinc/nginx-unprivileged:alpine AS final
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
