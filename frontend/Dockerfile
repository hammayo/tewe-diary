# Frontend image, one file for dev, test and deploy (PRD D17). Build context is frontend/.
#   docker build --target test frontend    lint, typecheck and unit tests
#   docker build frontend                  production image (nginx, port 8080)
#   docker compose --profile dev up frontend-dev   Vite dev server with hot reload
FROM node:22-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json .npmrc ./
RUN npm ci --ignore-scripts

FROM deps AS dev
COPY . .
EXPOSE 5173
CMD ["npm", "run", "dev", "--", "--host"]

FROM dev AS test
CMD ["npm", "run", "check"]

FROM dev AS build
RUN npm run build

FROM nginxinc/nginx-unprivileged:alpine AS final
# The nginx image fills ${...} in templates from the environment at start-up (compose overrides these).
ENV MOVIES_UPSTREAM=http://movies-api:8080 \
    IDENTITY_UPSTREAM=http://identity-api:8080 \
    HERO_SLUGS=
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
