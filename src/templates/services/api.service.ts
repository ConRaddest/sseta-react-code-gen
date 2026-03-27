// !!---------------------------------------------------!!
// !!---------- AUTO-GENERATED: Do not edit! -----------!!
// !!---------------------------------------------------!!

import { ApiResponse, FetchRequest, SearchResponse, ValidateResponse } from "@sseta/components"
import axios, { AxiosInstance, AxiosProgressEvent } from "axios"

// [[TYPE_IMPORTS]]

// api client
const Client = (): AxiosInstance => {
  const instance = axios.create({
    baseURL: process.env.NEXT_PUBLIC_API_BASE_URL,
    withCredentials: true,
    headers: {
      "Accept": "application/json",
      "Content-Type": "application/json",
    },
  })

  // Request interceptor to omit undefined and empty string fields
  instance.interceptors.request.use((config) => {
    if (config.data && typeof config.data === "object") {
      config.data = Object.fromEntries(Object.entries(config.data).filter(([_, value]) => value !== undefined && value !== ""))
    }
    return config
  })

  // pushes to logout if the cookie is expired and we are not on an auth endpoint
  instance.interceptors.response.use(
    (success) => {
      return Promise.resolve(success)
    },
    (error) => {
      const isAuthEndpoint = error.config?.url?.includes("/auth/")

      if (error.response?.status === 401 && !isAuthEndpoint) {
        window.location.href = "/logout"
      }
      return Promise.reject(error)
    }
  )

  return instance
}

// Multipart client for file uploads — bypasses the undefined-field interceptor
// so FormData is passed through untouched.
const MultipartClient = (onUploadProgress?: (event: AxiosProgressEvent) => void): AxiosInstance => {
  const instance = axios.create({
    baseURL: process.env.NEXT_PUBLIC_API_BASE_URL,
    withCredentials: true,
    headers: {
      "Accept": "application/json",
      "Content-Type": "multipart/form-data",
    },
    onUploadProgress,
  })

  instance.interceptors.response.use(
    (success) => Promise.resolve(success),
    (error) => {
      const isAuthEndpoint = error.config?.url?.includes("/auth/")

      if (error.response?.status === 401 && !isAuthEndpoint) {
        // window.location.href = "/logout"
      }
      return Promise.reject(error)
    }
  )

  return instance
}

// [[API_EXPORT]]
