# Context: apps/web
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json* ./
RUN npm install

COPY . .

# Variaveis do cliente OAuth sao embutidas no build (Vite).
ARG VITE_IDENTITY_AUTHORITY=http://localhost:5080
ARG VITE_CLIENT_ID=expense-tracking-spa
ARG VITE_REDIRECT_URI=http://localhost:5173/callback
ARG VITE_SCOPE="openid profile expenses.read expenses.write offline_access"
ARG VITE_API_BASE_URL=http://localhost:8080
ENV VITE_IDENTITY_AUTHORITY=$VITE_IDENTITY_AUTHORITY \
    VITE_CLIENT_ID=$VITE_CLIENT_ID \
    VITE_REDIRECT_URI=$VITE_REDIRECT_URI \
    VITE_SCOPE=$VITE_SCOPE \
    VITE_API_BASE_URL=$VITE_API_BASE_URL

RUN npm run build

FROM nginx:1.27-alpine AS final
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
