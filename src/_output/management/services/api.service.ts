// !!---------------------------------------------------------!!
// !!-------- AUTO-GENERATED: Edit in code generator! --------!!
// !!--------------- CHANGES HERE WILL BE LOST ---------------!!
// !!---------------------------------------------------------!!

import { ApiResponse, FetchRequest, SearchResponse, ValidateResponse } from "@sseta/components"
import axios, { AxiosInstance, AxiosProgressEvent } from "axios"

import type {
  AuthProfileResponse,

  AccessDepartmentTypeSearchResponse,
  AccessStaffRoleRequestCreateResponse,
  AccessStaffRoleRequestCreateRequest,
  AccessStaffRoleRequest,
  AccessStaffRoleRequestSearchResponse,

  AdminStaffRoleRequest,
  AdminStaffRoleRequestSearchResponse,
  AdminStaffRoleRequestSubmitRequest,
  AdminStaffRoleRequestUpdateRequest,
  AdminStaffRoleRequestValidateRequest,

  BRoleSearchResponse,

  EcdRoleSearchResponse,
  EcdSystemUserUpdateRequest,

  PmvrRoleSearchResponse,
  PmvrSystemUserUpdateRequest,

  SpRoleSearchResponse,
  SpSystemUserUpdateRequest,

  SpiRoleSearchResponse,
  SpiSystemUserUpdateRequest,
} from "../types/api.types"

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

export const Api = {
  // Auth
  Auth: {
    logout: async (): Promise<ApiResponse<unknown>> => {
      const response = await Client().get("/Auth/Logout")
      return response.data
    },
    profile: async (): Promise<ApiResponse<AuthProfileResponse>> => {
      const response = await Client().get("/Auth/Profile")
      return response.data
    },
  },

  ACCESS: {
    DepartmentType: {
        search: async (payload: FetchRequest): Promise<ApiResponse<SearchResponse<AccessDepartmentTypeSearchResponse>>> => {
          const response = await Client().post(`/management/ACCESS/DepartmentType/Search`, payload)
          return response.data
        },
    },
    StaffRoleRequest: {
        create: async (payload: AccessStaffRoleRequestCreateRequest): Promise<ApiResponse<AccessStaffRoleRequestCreateResponse>> => {
          const response = await Client().post(`/management/ACCESS/StaffRoleRequest/Create`, payload)
          return response.data
        },
        delete: async (id: number): Promise<ApiResponse<boolean>> => {
          const response = await Client().delete(`/management/ACCESS/StaffRoleRequest/Delete/${id}`)
          return response.data
        },
        retrieve: async (id: number): Promise<ApiResponse<AccessStaffRoleRequest>> => {
          const response = await Client().get(`/management/ACCESS/StaffRoleRequest/Retrieve/${id}`)
          return response.data
        },
        search: async (payload: FetchRequest): Promise<ApiResponse<SearchResponse<AccessStaffRoleRequestSearchResponse>>> => {
          const response = await Client().post(`/management/ACCESS/StaffRoleRequest/Search`, payload)
          return response.data
        },
    },
  },

  ADMIN: {
    StaffRoleRequest: {
        retrieve: async (id: number): Promise<ApiResponse<AdminStaffRoleRequest>> => {
          const response = await Client().get(`/management/ADMIN/StaffRoleRequest/Retrieve/${id}`)
          return response.data
        },
        search: async (payload: FetchRequest): Promise<ApiResponse<SearchResponse<AdminStaffRoleRequestSearchResponse>>> => {
          const response = await Client().post(`/management/ADMIN/StaffRoleRequest/Search`, payload)
          return response.data
        },
        submit: async (payload: AdminStaffRoleRequestSubmitRequest): Promise<ApiResponse<boolean>> => {
          const response = await Client().post(`/management/ADMIN/StaffRoleRequest/Submit`, payload)
          return response.data
        },
        update: async (payload: AdminStaffRoleRequestUpdateRequest): Promise<ApiResponse<boolean>> => {
          const response = await Client().put(`/management/ADMIN/StaffRoleRequest/Update`, payload)
          return response.data
        },
        validate: async (payload: AdminStaffRoleRequestValidateRequest): Promise<ApiResponse<ValidateResponse>> => {
          const response = await Client().post(`/management/ADMIN/StaffRoleRequest/Validate`, payload)
          return response.data
        },
    },
  },

  B: {
    Role: {
        search: async (payload: FetchRequest): Promise<ApiResponse<SearchResponse<BRoleSearchResponse>>> => {
          const response = await Client().post(`/management/B/Role/Search`, payload)
          return response.data
        },
    },
  },

  ECD: {
    Role: {
        search: async (payload: FetchRequest): Promise<ApiResponse<SearchResponse<EcdRoleSearchResponse>>> => {
          const response = await Client().post(`/management/ECD/Role/Search`, payload)
          return response.data
        },
    },
    SystemUser: {
        update: async (payload: EcdSystemUserUpdateRequest): Promise<ApiResponse<boolean>> => {
          const response = await Client().put(`/management/ECD/SystemUser/Update`, payload)
          return response.data
        },
    },
  },

  PMVR: {
    Role: {
        search: async (payload: FetchRequest): Promise<ApiResponse<SearchResponse<PmvrRoleSearchResponse>>> => {
          const response = await Client().post(`/management/PMVR/Role/Search`, payload)
          return response.data
        },
    },
    SystemUser: {
        update: async (payload: PmvrSystemUserUpdateRequest): Promise<ApiResponse<boolean>> => {
          const response = await Client().put(`/management/PMVR/SystemUser/Update`, payload)
          return response.data
        },
    },
  },

  SP: {
    Role: {
        search: async (payload: FetchRequest): Promise<ApiResponse<SearchResponse<SpRoleSearchResponse>>> => {
          const response = await Client().post(`/management/SP/Role/Search`, payload)
          return response.data
        },
    },
    SystemUser: {
        update: async (payload: SpSystemUserUpdateRequest): Promise<ApiResponse<boolean>> => {
          const response = await Client().put(`/management/SP/SystemUser/Update`, payload)
          return response.data
        },
    },
  },

  SPI: {
    Role: {
        search: async (payload: FetchRequest): Promise<ApiResponse<SearchResponse<SpiRoleSearchResponse>>> => {
          const response = await Client().post(`/management/SPI/Role/Search`, payload)
          return response.data
        },
    },
    SystemUser: {
        update: async (payload: SpiSystemUserUpdateRequest): Promise<ApiResponse<boolean>> => {
          const response = await Client().put(`/management/SPI/SystemUser/Update`, payload)
          return response.data
        },
    },
  },

}
